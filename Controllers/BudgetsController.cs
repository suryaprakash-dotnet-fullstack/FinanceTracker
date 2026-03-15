using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using FinanceTracker.Data;
using FinanceTracker.Models;
using FinanceTracker.ViewModels;

namespace FinanceTracker.Controllers;

[Authorize]
public class BudgetsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private string UserId => _userManager.GetUserId(User)!;

    public BudgetsController(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index(int? month, int? year)
    {
        month ??= DateTime.Now.Month;
        year ??= DateTime.Now.Year;

        var budgets = await _db.Budgets
            .Include(b => b.Category)
            .Where(b => b.UserId == UserId && b.Month == month && b.Year == year)
            .ToListAsync();

        // Calculate spent for each budget
        var monthStart = new DateTime(year.Value, month.Value, 1);
        var monthEnd = monthStart.AddMonths(1);
        var transactions = await _db.Transactions
            .Where(t => t.UserId == UserId && t.Type == TransactionType.Expense
                && t.Date >= monthStart && t.Date < monthEnd)
            .ToListAsync();

        foreach (var b in budgets)
        {
            b.Spent = transactions.Where(t => t.CategoryId == b.CategoryId).Sum(t => t.Amount);
        }

        ViewBag.Month = month;
        ViewBag.Year = year;
        ViewBag.TotalBudget = budgets.Sum(b => b.Amount);
        ViewBag.TotalSpent = budgets.Sum(b => b.Spent);
        return View(budgets);
    }

    public async Task<IActionResult> Create()
    {
        var categories = await _db.Categories
            .Where(c => (c.IsSystem || c.UserId == UserId) && c.Type == TransactionType.Expense)
            .ToListAsync();
        ViewBag.Categories = new SelectList(categories, "Id", "Name");
        return View(new BudgetViewModel { Month = DateTime.Now.Month, Year = DateTime.Now.Year });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(BudgetViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            var cats = await _db.Categories.Where(c => (c.IsSystem || c.UserId == UserId) && c.Type == TransactionType.Expense).ToListAsync();
            ViewBag.Categories = new SelectList(cats, "Id", "Name");
            return View(vm);
        }

        var budget = new Budget
        {
            Name = vm.Name, Amount = vm.Amount, CategoryId = vm.CategoryId,
            Month = vm.Month, Year = vm.Year, RolloverEnabled = vm.RolloverEnabled,
            Notes = vm.Notes, UserId = UserId
        };
        _db.Budgets.Add(budget);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Budget created!";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var b = await _db.Budgets.FirstOrDefaultAsync(x => x.Id == id && x.UserId == UserId);
        if (b != null) { _db.Budgets.Remove(b); await _db.SaveChangesAsync(); }
        TempData["Success"] = "Budget deleted.";
        return RedirectToAction(nameof(Index));
    }
}
