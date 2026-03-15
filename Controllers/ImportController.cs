using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using FinanceTracker.Data;
using FinanceTracker.Models;
using System.Text.RegularExpressions;

namespace FinanceTracker.Controllers;

[Authorize]
public class ImportController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private string UserId => _userManager.GetUserId(User)!;

    public ImportController(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
    }

    public async Task<IActionResult> Index()
    {
        var accounts = await _db.Accounts.Where(a => a.UserId == UserId && a.IsActive).ToListAsync();
        ViewBag.Accounts = accounts;
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Upload(IFormFile file, int accountId)
    {
        var accounts = await _db.Accounts.Where(a => a.UserId == UserId && a.IsActive).ToListAsync();
        ViewBag.Accounts = accounts;

        if (file == null || file.Length == 0) { ViewBag.Error = "Please select a file."; return View("Index"); }
        var ext = Path.GetExtension(file.FileName).ToLower();
        if (ext != ".xlsx" && ext != ".xls") { ViewBag.Error = "Only Excel files (.xlsx, .xls) are supported."; return View("Index"); }

        var account = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId && a.UserId == UserId);
        if (account == null) { ViewBag.Error = "Invalid account selected."; return View("Index"); }

        var parsed = new List<ParsedTransaction>();

        try
        {
            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            stream.Position = 0;

            using var package = new ExcelPackage(stream);
            var ws = package.Workbook.Worksheets[0];

            var categories = await _db.Categories.Where(c => c.IsSystem || c.UserId == UserId).ToListAsync();

            var uncategorizedExpense = categories.FirstOrDefault(c => c.Name == "Uncategorized" && c.Type == TransactionType.Expense)
                ?? categories.First(c => c.Type == TransactionType.Expense);
            var uncategorizedIncome = categories.FirstOrDefault(c => c.Name == "Other Income" && c.Type == TransactionType.Income)
                ?? categories.First(c => c.Type == TransactionType.Income);

            for (int r = 1; r <= ws.Dimension.End.Row; r++)
            {
                var col2 = ws.Cells[r, 2].Text?.Trim();
                if (string.IsNullOrEmpty(col2) || col2 == "Date") continue;
                if (!TryParseDate(col2, out var txDate)) continue;

                var details = ws.Cells[r, 3].Text?.Trim() ?? "";
                int nr = r + 1;
                while (nr <= ws.Dimension.End.Row)
                {
                    var nextDate = ws.Cells[nr, 2].Text?.Trim();
                    var nextDetails = ws.Cells[nr, 3].Text?.Trim();
                    if (!string.IsNullOrEmpty(nextDetails) && string.IsNullOrEmpty(nextDate)) { details += " " + nextDetails; nr++; }
                    else break;
                }

                decimal debit = ParseAmount(ws.Cells[r, 9].Text?.Trim());
                decimal credit = ParseAmount(ws.Cells[r, 11].Text?.Trim());
                if (debit == 0 && credit == 0) continue;

                var type = credit > 0 ? TransactionType.Income : TransactionType.Expense;
                var amount = credit > 0 ? credit : debit;
                var category = AutoCategorize(details, type, categories, uncategorizedExpense, uncategorizedIncome);

                parsed.Add(new ParsedTransaction
                {
                    Date = txDate,
                    Description = ExtractDescription(details),
                    FullDetails = details,
                    Amount = amount,
                    Type = type,
                    CategoryId = category.Id,
                    CategoryName = category.Name,
                    CategoryIcon = category.Icon,
                    IsUncategorized = category.Name is "Uncategorized" or "Other Income"
                });
            }

            if (!parsed.Any()) { ViewBag.Error = "No transactions found in the file."; return View("Index"); }

            int unc = parsed.Count(p => p.IsUncategorized);
            if (unc > 0)
                ViewBag.UncategorizedWarning = $"{unc} transaction(s) could not be auto-categorized and are marked \"Uncategorized\". Please update them below before confirming.";

            ViewBag.Parsed = parsed;
            ViewBag.AccountId = accountId;
            ViewBag.AccountName = account.Name;
            ViewBag.Categories = categories;
            return View("Preview");
        }
        catch (Exception ex) { ViewBag.Error = $"Error reading file: {ex.Message}"; return View("Index"); }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(List<ImportRow> rows, int accountId)
    {
        var account = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId && a.UserId == UserId);
        if (account == null) return NotFound();

        int imported = 0, skipped = 0;
        foreach (var row in rows)
        {
            if (!row.Import) { skipped++; continue; }
            _db.Transactions.Add(new Transaction
            {
                Amount = row.Amount,
                Description = row.Description,
                Date = row.Date,
                Type = row.Type,
                CategoryId = row.CategoryId,
                AccountId = accountId,
                Notes = "Imported from bank statement",
                UserId = UserId
            });
            account.Balance += row.Type == TransactionType.Income ? row.Amount : -row.Amount;
            imported++;
        }

        await _db.SaveChangesAsync();

        foreach (var catId in rows.Where(r => r.Import && r.Type == TransactionType.Expense).Select(r => r.CategoryId).Distinct())
        {
            var now = DateTime.Now;
            var ms = new DateTime(now.Year, now.Month, 1);
            var budget = await _db.Budgets.FirstOrDefaultAsync(b => b.UserId == UserId && b.CategoryId == catId && b.Month == now.Month && b.Year == now.Year);
            if (budget != null)
                budget.Spent = await _db.Transactions.Where(t => t.UserId == UserId && t.CategoryId == catId && t.Type == TransactionType.Expense && t.Date >= ms && t.Date < ms.AddMonths(1)).SumAsync(t => t.Amount);
        }

        await _db.SaveChangesAsync();
        TempData["Success"] = $"Successfully imported {imported} transactions. {skipped} skipped.";
        return RedirectToAction("Index", "Transactions");
    }

    private static bool TryParseDate(string text, out DateTime date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string[] formats = { "dd MMM yyyy", "d MMM yyyy", "dd/MM/yyyy", "d/MM/yyyy", "yyyy-MM-dd" };
        return DateTime.TryParseExact(text.Trim(), formats, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out date);
    }

    private static decimal ParseAmount(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text == "-" || text == " - ") return 0;
        return decimal.TryParse(Regex.Replace(text, @"[INR,\s]", ""), out var val) ? val : 0;
    }

    private static string ExtractDescription(string details)
    {
        var parts = details.Split('/');
        if (parts.Length >= 2) { var n = parts[1].Trim(); if (n.Length > 1) return n; }
        return details.Length > 80 ? details[..80] : details;
    }

    private static Category AutoCategorize(string details, TransactionType type, List<Category> categories, Category uncategorizedExpense, Category uncategorizedIncome)
    {
        var d = details.ToLower();

        if (type == TransactionType.Income)
        {
            if (d.Contains("salary") || d.Contains("payroll")) return Cat(categories, "Salary", TransactionType.Income) ?? uncategorizedIncome;
            if (d.Contains("freelance")) return Cat(categories, "Freelance", TransactionType.Income) ?? uncategorizedIncome;
            return uncategorizedIncome;
        }

        if (d.Contains("petrol") || d.Contains("fuel") || d.Contains("filling station") || d.Contains("diesel") || d.Contains("pump"))
            return Cat(categories, "Transportation") ?? uncategorizedExpense;
        if (d.Contains("hotel") || d.Contains("restaurant") || d.Contains("food") || d.Contains("snack") || d.Contains("coffee") || d.Contains("cafe") || d.Contains("bakery") || d.Contains("juice") || d.Contains("biryani") || d.Contains("sweets") || d.Contains("veg"))
            return Cat(categories, "Food & Dining") ?? uncategorizedExpense;
        if (d.Contains("amazon") || d.Contains("flipkart") || d.Contains("shop") || d.Contains("store") || d.Contains("mart") || d.Contains("bazaar") || d.Contains("mall"))
            return Cat(categories, "Shopping") ?? uncategorizedExpense;
        if (d.Contains("electricity") || d.Contains("tneb") || d.Contains("bsnl") || d.Contains("airtel") || d.Contains("jio") || d.Contains("recharge") || d.Contains("mobile") || d.Contains("internet") || d.Contains("broadband"))
            return Cat(categories, "Utilities") ?? uncategorizedExpense;
        if (d.Contains("hospital") || d.Contains("pharmacy") || d.Contains("medical") || d.Contains("doctor") || d.Contains("clinic") || d.Contains("apollo") || d.Contains("medplus"))
            return Cat(categories, "Healthcare") ?? uncategorizedExpense;
        if (d.Contains("school") || d.Contains("college") || d.Contains("fees") || d.Contains("education") || d.Contains("tuition"))
            return Cat(categories, "Education") ?? uncategorizedExpense;
        if (d.Contains("rent") || d.Contains("house") || d.Contains("flat") || d.Contains("hostel") || d.Contains("room"))
            return Cat(categories, "Housing & Rent") ?? uncategorizedExpense;
        if (d.Contains("theatre") || d.Contains("movie") || d.Contains("cinema") || d.Contains("netflix") || d.Contains("hotstar") || d.Contains("spotify"))
            return Cat(categories, "Entertainment") ?? uncategorizedExpense;
        if (d.Contains("irctc") || d.Contains("railway") || d.Contains("flight") || d.Contains("ola") || d.Contains("uber") || d.Contains("rapido") || d.Contains("redbus"))
            return Cat(categories, "Travel") ?? uncategorizedExpense;
        if (d.Contains("insurance") || d.Contains("lic ") || d.Contains("policy") || d.Contains("premium"))
            return Cat(categories, "Insurance") ?? uncategorizedExpense;

        return uncategorizedExpense;
    }

    private static Category? Cat(List<Category> cats, string name, TransactionType type = TransactionType.Expense)
        => cats.FirstOrDefault(c => c.Name == name && c.Type == type);
}

public class ParsedTransaction
{
    public DateTime Date { get; set; }
    public string Description { get; set; } = string.Empty;
    public string FullDetails { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public TransactionType Type { get; set; }
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string CategoryIcon { get; set; } = string.Empty;
    public bool IsUncategorized { get; set; }
}

public class ImportRow
{
    public bool Import { get; set; } = true;
    public DateTime Date { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public TransactionType Type { get; set; }
    public int CategoryId { get; set; }
}