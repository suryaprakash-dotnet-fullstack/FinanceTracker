using FinanceTracker.Data;
using FinanceTracker.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NPOI.HSSF.UserModel;
using NPOI.POIFS.Crypt;
using NPOI.POIFS.FileSystem;
using NPOI.SS.UserModel;                      // dotnet add package NPOI
using NPOI.XSSF.UserModel;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;                        // dotnet add package PdfPig
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Exceptions;

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
    }

    // ── GET /Import ────────────────────────────────────────────────────────────
    public async Task<IActionResult> Index()
    {
        ViewBag.Accounts = await _db.Accounts
            .Where(a => a.UserId == UserId && a.IsActive).ToListAsync();
        return View();
    }

    // ── POST /Import/Upload ────────────────────────────────────────────────────
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Upload(IFormFile file, int accountId, string? password)
    {
        ViewBag.Accounts = await _db.Accounts
            .Where(a => a.UserId == UserId && a.IsActive).ToListAsync();

        if (file == null || file.Length == 0)
        { ViewBag.Error = "Please select a file."; return View("Index"); }

        var ext = Path.GetExtension(file.FileName).ToLower();
        if (ext != ".xlsx" && ext != ".xls" && ext != ".pdf" && ext != ".csv")
        { ViewBag.Error = "Supported formats: .xlsx, .xls, .pdf, .csv"; return View("Index"); }

        var account = await _db.Accounts
            .FirstOrDefaultAsync(a => a.Id == accountId && a.UserId == UserId);
        if (account == null)
        { ViewBag.Error = "Invalid account selected."; return View("Index"); }

        // Read file bytes once — needed for password retry detection
        using var stream = new MemoryStream();
        await file.CopyToAsync(stream);
        var fileBytes = stream.ToArray();

        var categories = await _db.Categories
            .Where(c => c.IsSystem || c.UserId == UserId).ToListAsync();

        var uncategorizedExpense =
            categories.FirstOrDefault(c => c.Name == "Uncategorized" && c.Type == TransactionType.Expense)
            ?? categories.First(c => c.Type == TransactionType.Expense);
        var uncategorizedIncome =
            categories.FirstOrDefault(c => c.Name == "Other Income" && c.Type == TransactionType.Income)
            ?? categories.First(c => c.Type == TransactionType.Income);

        List<ParsedTransaction> parsed;

        try
        {
            parsed = ext switch
            {
                ".xlsx" or ".xls" => ParseExcel(fileBytes, password, categories, uncategorizedExpense, uncategorizedIncome),
                ".pdf" => ParsePdf(fileBytes, password, categories, uncategorizedExpense, uncategorizedIncome),
                ".csv" => ParseCsv(fileBytes, categories, uncategorizedExpense, uncategorizedIncome),
                _ => throw new InvalidOperationException("Unsupported format.")
            };
        }
        catch (PasswordRequiredException)
        {
            // File is protected and no password was supplied — show password prompt
            ViewBag.PasswordRequired = true;
            ViewBag.FileName = file.FileName;
            ViewBag.AccountId = accountId;
            ViewBag.Error = "This file is password-protected. Please enter the password to continue.";
            return View("Index");
        }
        catch (InvalidPasswordException)
        {
            ViewBag.PasswordRequired = true;
            ViewBag.FileName = file.FileName;
            ViewBag.AccountId = accountId;
            ViewBag.Error = "Incorrect password. Please try again.";
            return View("Index");
        }
        catch (Exception ex)
        {
            ViewBag.Error = $"Error reading file: {ex.Message}";
            return View("Index");
        }

        if (!parsed.Any())
        { ViewBag.Error = "No transactions found in the file. Check the format matches the expected layout."; return View("Index"); }

        int unc = parsed.Count(p => p.IsUncategorized);
        if (unc > 0)
            ViewBag.UncategorizedWarning =
                $"{unc} transaction(s) could not be auto-categorized. Please update them below before confirming.";

        ViewBag.Parsed = parsed;
        ViewBag.AccountId = accountId;
        ViewBag.AccountName = account.Name;
        ViewBag.Categories = categories;
        return View("Preview");
    }

    // ── POST /Import/Confirm ───────────────────────────────────────────────────
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(List<ImportRow> rows, int accountId)
    {
        var account = await _db.Accounts
            .FirstOrDefaultAsync(a => a.Id == accountId && a.UserId == UserId);
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

        foreach (var catId in rows
            .Where(r => r.Import && r.Type == TransactionType.Expense)
            .Select(r => r.CategoryId).Distinct())
        {
            var now = DateTime.Now;
            var monthStart = new DateTime(now.Year, now.Month, 1);
            var budget = await _db.Budgets.FirstOrDefaultAsync(b =>
                b.UserId == UserId && b.CategoryId == catId &&
                b.Month == now.Month && b.Year == now.Year);

            if (budget != null)
                budget.Spent = await _db.Transactions
                    .Where(t => t.UserId == UserId && t.CategoryId == catId &&
                                t.Type == TransactionType.Expense &&
                                t.Date >= monthStart && t.Date < monthStart.AddMonths(1))
                    .SumAsync(t => t.Amount);
        }

        await _db.SaveChangesAsync();
        TempData["Success"] = $"Successfully imported {imported} transactions. {skipped} skipped.";
        return RedirectToAction("Index", "Transactions");
    }

    // ════════════════════════════════════════════════════════════════════════════
    // PARSERS
    // ════════════════════════════════════════════════════════════════════════════

    // ── Excel (NPOI) — supports password-protected .xls and .xlsx ───────────
    // Package: dotnet add package NPOI
    //
    // How password decryption works in NPOI:
    //   Both encrypted .xls and .xlsx are stored as OLE2/POIFS containers.
    //   POIFSFileSystem detects this; Decryptor unwraps the inner workbook stream.
    //   Unprotected .xlsx files are plain ZIP — WorkbookFactory handles them directly.
    private static List<ParsedTransaction> ParseExcel(
        byte[] bytes, string? password,
        List<Category> categories, Category uncExp, Category uncInc)
    {
        IWorkbook workbook;

        try
        {
            // Strategy: try opening as an OLE2/POIFS container first.
            // Encrypted Excel files (.xls and .xlsx) are always OLE2 containers.
            // Plain .xlsx files are ZIP-based and will throw on POIFSFileSystem — we catch that.
            try
            {
                using var msOle = new MemoryStream(bytes);
                var poifs = new POIFSFileSystem(msOle);   // throws if not OLE2 (e.g. plain .xlsx)

                if (poifs.Root.HasEntry("EncryptionInfo"))
                {
                    // File is password-encrypted
                    if (string.IsNullOrWhiteSpace(password))
                        throw new PasswordRequiredException();

                    var info = new EncryptionInfo(poifs);
                    var decryptor = Decryptor.GetInstance(info);

                    if (!decryptor.VerifyPassword(password))
                        throw new InvalidPasswordException();

                    using var decStream = decryptor.GetDataStream(poifs);
                    workbook = WorkbookFactory.Create(decStream);
                }
                else
                {
                    // Unencrypted .xls (OLE2 but no encryption entry)
                    using var msXls = new MemoryStream(bytes);
                    workbook = WorkbookFactory.Create(msXls);
                }
            }
            catch (PasswordRequiredException) { throw; }
            catch (InvalidPasswordException) { throw; }
            catch
            {
                // Not an OLE2 file — must be a plain (unencrypted) .xlsx ZIP
                using var msXlsx = new MemoryStream(bytes);
                workbook = WorkbookFactory.Create(msXlsx);
            }
        }
        catch (PasswordRequiredException) { throw; }
        catch (InvalidPasswordException) { throw; }
        catch (Exception ex) when (
            ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("encrypted", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("protected", StringComparison.OrdinalIgnoreCase))
        {
            throw string.IsNullOrWhiteSpace(password)
                ? new PasswordRequiredException()
                : (Exception)new InvalidPasswordException();
        }

        using (workbook)
        {
            var sheet = workbook.GetSheetAt(0);
            var parsed = new List<ParsedTransaction>();
            var lastRow = sheet.LastRowNum;

            for (int r = 0; r <= lastRow; r++)
            {
                var row = sheet.GetRow(r);
                if (row == null) continue;

                // Columns are 0-based in NPOI; bank statement layout:
                //   col 1 (B) = Date, col 2 (C) = Details,
                //   col 8 (I) = Debit, col 10 (K) = Credit
                var col2 = row.GetCell(1)?.ToString()?.Trim();
                if (string.IsNullOrEmpty(col2) || col2 == "Date") continue;
                if (!TryParseDate(col2, out var txDate)) continue;

                var details = row.GetCell(2)?.ToString()?.Trim() ?? "";

                // Merge continuation rows (rows with details but no date)
                int nr = r + 1;
                while (nr <= lastRow)
                {
                    var nextRow = sheet.GetRow(nr);
                    var nextDate = nextRow?.GetCell(1)?.ToString()?.Trim();
                    var nextDetails = nextRow?.GetCell(2)?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(nextDetails) && string.IsNullOrEmpty(nextDate))
                    { details += " " + nextDetails; nr++; }
                    else break;
                }

                decimal debit = ParseAmount(row.GetCell(8)?.ToString()?.Trim());
                decimal credit = ParseAmount(row.GetCell(10)?.ToString()?.Trim());
                if (debit == 0 && credit == 0) continue;

                AddParsed(parsed, txDate, details, credit, debit, categories, uncExp, uncInc);
            }
            return parsed;
        }
    }

    // ── PDF ────────────────────────────────────────────────────────────────────
    // PdfPig extracts raw text line-by-line. We re-join lines and look for the
    // same date + debit/credit pattern used by most Indian bank PDF statements.
    private static List<ParsedTransaction> ParsePdf(
        byte[] bytes, string? password,
        List<Category> categories, Category uncExp, Category uncInc)
    {
        PdfDocument doc;
        try
        {
            doc = string.IsNullOrWhiteSpace(password)
                ? PdfDocument.Open(bytes)
                : PdfDocument.Open(bytes, new ParsingOptions { Password = password });
        }
        catch (PdfDocumentEncryptedException)
        {
            if (string.IsNullOrWhiteSpace(password))
                throw new PasswordRequiredException();
            throw new InvalidPasswordException();
        }
        catch (Exception ex) when (
            ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("encrypted", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(password))
                throw new PasswordRequiredException();
            throw new InvalidPasswordException();
        }

        using (doc)
        {
            // Collect all text lines across all pages
            var lines = new List<string>();
            foreach (var page in doc.GetPages())
            {
                var pageText = page.Text;
                lines.AddRange(pageText
                    .Split('\n')
                    .Select(l => l.Trim())
                    .Where(l => !string.IsNullOrWhiteSpace(l)));
            }

            return ParseTextLines(lines, categories, uncExp, uncInc);
        }
    }

    // ── CSV ────────────────────────────────────────────────────────────────────
    // Supports two layouts:
    //   Layout A (Indian Bank style): col0=Sr, col1=Date, col2=Details, col8=Debit, col10=Credit
    //   Layout B (generic):           headers detected — Date, Description/Narration, Debit/Withdrawal, Credit/Deposit
    private static List<ParsedTransaction> ParseCsv(
        byte[] bytes,
        List<Category> categories, Category uncExp, Category uncInc)
    {
        var parsed = new List<ParsedTransaction>();
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        string? headerLine = null;
        int[]? colMap = null;   // [dateCol, descCol, debitCol, creditCol]
        bool headerMode = false;

        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(line)) continue;

            var cols = SplitCsv(line);

            // Detect header row
            if (colMap == null)
            {
                var lower = cols.Select(c => c.ToLower()).ToArray();
                int dateCol = Array.FindIndex(lower, c => c.Contains("date"));
                int descCol = Array.FindIndex(lower, c => c.Contains("narration") || c.Contains("description") || c.Contains("details") || c.Contains("particulars"));
                int debitCol = Array.FindIndex(lower, c => c.Contains("debit") || c.Contains("withdrawal") || c.Contains("dr"));
                int creditCol = Array.FindIndex(lower, c => c.Contains("credit") || c.Contains("deposit") || c.Contains("cr"));

                if (dateCol >= 0 && descCol >= 0 && (debitCol >= 0 || creditCol >= 0))
                {
                    colMap = new[] { dateCol, descCol, debitCol >= 0 ? debitCol : -1, creditCol >= 0 ? creditCol : -1 };
                    headerMode = true;
                    headerLine = line;
                    continue;
                }

                // Fallback: assume Indian Bank fixed layout (col indices 1,2,8,10)
                colMap = new[] { 1, 2, 8, 10 };
            }

            // Skip re-encountered header
            if (headerMode && line == headerLine) continue;

            if (cols.Length <= Math.Max(colMap[0], colMap[1])) continue;

            var dateStr = SafeGet(cols, colMap[0]);
            var details = SafeGet(cols, colMap[1]);
            var debitStr = colMap[2] >= 0 ? SafeGet(cols, colMap[2]) : "";
            var creditStr = colMap[3] >= 0 ? SafeGet(cols, colMap[3]) : "";

            if (!TryParseDate(dateStr, out var txDate)) continue;

            decimal debit = ParseAmount(debitStr);
            decimal credit = ParseAmount(creditStr);
            if (debit == 0 && credit == 0) continue;

            AddParsed(parsed, txDate, details, credit, debit, categories, uncExp, uncInc);
        }

        return parsed;
    }

    // ── Text line parser (used by PDF) ────────────────────────────────────────
    // Looks for lines that start with a recognisable date pattern followed by
    // amounts at the end. Works with most Indian bank PDF statement layouts.
    private static readonly Regex _lineRx = new(
        @"(\d{1,2}[\/\- ][A-Za-z]{3}[\/\- ]\d{4}|\d{2}[\/\-]\d{2}[\/\-]\d{4})" + // date
        @"(.+?)" +                                                                    // description
        @"([\d,]+\.\d{2})?\s+([\d,]+\.\d{2})?\s+([\d,]+\.\d{2})",                  // debit / credit / balance
        RegexOptions.Compiled);

    private static List<ParsedTransaction> ParseTextLines(
        List<string> lines,
        List<Category> categories, Category uncExp, Category uncInc)
    {
        var parsed = new List<ParsedTransaction>();

        foreach (var line in lines)
        {
            var m = _lineRx.Match(line);
            if (!m.Success) continue;

            if (!TryParseDate(m.Groups[1].Value.Trim(), out var txDate)) continue;

            var details = m.Groups[2].Value.Trim();
            decimal debit = 0, credit = 0;

            // Groups 3, 4 = debit/credit (one may be empty), group 5 = balance
            var g3 = ParseAmount(m.Groups[3].Value);
            var g4 = ParseAmount(m.Groups[4].Value);

            // Heuristic: smaller of the two non-zero values beside balance = transaction,
            // context from description decides income/expense
            var d = details.ToLower();
            bool looksLikeIncome = d.Contains("salary") || d.Contains("credit") ||
                                   d.Contains("neft cr") || d.Contains("imps cr");

            if (looksLikeIncome) { credit = g3 > 0 ? g3 : g4; }
            else { debit = g3 > 0 ? g3 : g4; }

            if (debit == 0 && credit == 0) continue;

            AddParsed(parsed, txDate, details, credit, debit, categories, uncExp, uncInc);
        }

        return parsed;
    }

    // ════════════════════════════════════════════════════════════════════════════
    // SHARED HELPERS
    // ════════════════════════════════════════════════════════════════════════════

    private static void AddParsed(
        List<ParsedTransaction> list,
        DateTime txDate, string details,
        decimal credit, decimal debit,
        List<Category> categories, Category uncExp, Category uncInc)
    {
        var type = credit > 0 ? TransactionType.Income : TransactionType.Expense;
        var amount = credit > 0 ? credit : debit;
        var category = AutoCategorize(details, type, categories, uncExp, uncInc);

        list.Add(new ParsedTransaction
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

    private static bool TryParseDate(string text, out DateTime date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string[] formats =
        {
            "dd MMM yyyy", "d MMM yyyy",
            "dd/MM/yyyy",  "d/MM/yyyy",
            "dd-MM-yyyy",  "d-MM-yyyy",
            "yyyy-MM-dd",  "MM/dd/yyyy"
        };
        return DateTime.TryParseExact(
            text.Trim(), formats,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out date);
    }

    private static decimal ParseAmount(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text == "-" || text == " - ") return 0;
        return decimal.TryParse(Regex.Replace(text, @"[INR₹,\s]", ""), out var val) ? val : 0;
    }

    private static string ExtractDescription(string details)
    {
        var parts = details.Split('/');
        if (parts.Length >= 2) { var n = parts[1].Trim(); if (n.Length > 1) return n; }
        return details.Length > 80 ? details[..80] : details;
    }

    // Minimal RFC-4180 CSV splitter (handles quoted fields with commas inside)
    private static string[] SplitCsv(string line)
    {
        var result = new List<string>();
        bool inQuotes = false;
        var current = new StringBuilder();
        foreach (char c in line)
        {
            if (c == '"') { inQuotes = !inQuotes; }
            else if (c == ',' && !inQuotes) { result.Add(current.ToString().Trim()); current.Clear(); }
            else { current.Append(c); }
        }
        result.Add(current.ToString().Trim());
        return result.ToArray();
    }

    private static string SafeGet(string[] cols, int idx)
        => idx >= 0 && idx < cols.Length ? cols[idx].Trim() : "";

    private static Category AutoCategorize(
        string details, TransactionType type,
        List<Category> categories, Category uncExp, Category uncInc)
    {
        var d = details.ToLower();

        if (type == TransactionType.Income)
        {
            if (d.Contains("salary") || d.Contains("payroll")) return Cat(categories, "Salary", TransactionType.Income) ?? uncInc;
            if (d.Contains("freelance")) return Cat(categories, "Freelance", TransactionType.Income) ?? uncInc;
            return uncInc;
        }

        if (d.Contains("petrol") || d.Contains("fuel") || d.Contains("diesel") || d.Contains("pump"))
            return Cat(categories, "Transportation") ?? uncExp;
        if (d.Contains("hotel") || d.Contains("restaurant") || d.Contains("food") || d.Contains("snack") ||
            d.Contains("coffee") || d.Contains("cafe") || d.Contains("bakery") || d.Contains("juice") ||
            d.Contains("biryani") || d.Contains("sweets") || d.Contains("zomato") || d.Contains("swiggy"))
            return Cat(categories, "Food & Dining") ?? uncExp;
        if (d.Contains("amazon") || d.Contains("flipkart") || d.Contains("shop") || d.Contains("store") ||
            d.Contains("mart") || d.Contains("bazaar") || d.Contains("mall") || d.Contains("myntra"))
            return Cat(categories, "Shopping") ?? uncExp;
        if (d.Contains("electricity") || d.Contains("tneb") || d.Contains("bsnl") || d.Contains("airtel") ||
            d.Contains("jio") || d.Contains("recharge") || d.Contains("mobile") || d.Contains("broadband"))
            return Cat(categories, "Utilities") ?? uncExp;
        if (d.Contains("hospital") || d.Contains("pharmacy") || d.Contains("medical") || d.Contains("doctor") ||
            d.Contains("clinic") || d.Contains("apollo") || d.Contains("medplus"))
            return Cat(categories, "Healthcare") ?? uncExp;
        if (d.Contains("school") || d.Contains("college") || d.Contains("fees") || d.Contains("tuition"))
            return Cat(categories, "Education") ?? uncExp;
        if (d.Contains("rent") || d.Contains("house") || d.Contains("flat") || d.Contains("hostel"))
            return Cat(categories, "Housing & Rent") ?? uncExp;
        if (d.Contains("theatre") || d.Contains("movie") || d.Contains("cinema") || d.Contains("netflix") ||
            d.Contains("hotstar") || d.Contains("spotify") || d.Contains("youtube"))
            return Cat(categories, "Entertainment") ?? uncExp;
        if (d.Contains("irctc") || d.Contains("railway") || d.Contains("flight") || d.Contains("ola") ||
            d.Contains("uber") || d.Contains("rapido") || d.Contains("redbus") || d.Contains("makemytrip"))
            return Cat(categories, "Travel") ?? uncExp;
        if (d.Contains("insurance") || d.Contains("lic ") || d.Contains("policy") || d.Contains("premium"))
            return Cat(categories, "Insurance") ?? uncExp;

        return uncExp;
    }

    private static Category? Cat(List<Category> cats, string name, TransactionType type = TransactionType.Expense)
        => cats.FirstOrDefault(c => c.Name == name && c.Type == type);
}

// ── Custom exceptions for clean password error handling ───────────────────────
public class PasswordRequiredException : Exception { }
public class InvalidPasswordException : Exception { }

// ── DTOs ──────────────────────────────────────────────────────────────────────
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