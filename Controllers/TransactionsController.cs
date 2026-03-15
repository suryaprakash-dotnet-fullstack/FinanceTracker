using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using FinanceTracker.Data;
using FinanceTracker.Models;
using FinanceTracker.Services;
using FinanceTracker.ViewModels;

namespace FinanceTracker.Controllers;

[Authorize]
public class TransactionsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly FinanceService _financeService;

    public TransactionsController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, FinanceService financeService)
    {
        _db = db;
        _userManager = userManager;
        _financeService = financeService;
    }

    private string UserId => _userManager.GetUserId(User)!;

    public async Task<IActionResult> Index(string? type, int? categoryId, int? accountId,
        DateTime? from, DateTime? to, string? search, int page = 1)
    {
        var query = _db.Transactions
            .Include(t => t.Category)
            .Include(t => t.Account)
            .Where(t => t.UserId == UserId);

        if (!string.IsNullOrEmpty(type) && Enum.TryParse<TransactionType>(type, out var tt))
            query = query.Where(t => t.Type == tt);
        if (categoryId.HasValue)
            query = query.Where(t => t.CategoryId == categoryId);
        if (accountId.HasValue)
            query = query.Where(t => t.AccountId == accountId);
        if (from.HasValue)
            query = query.Where(t => t.Date >= from.Value);
        if (to.HasValue)
            query = query.Where(t => t.Date <= to.Value);
        if (!string.IsNullOrEmpty(search))
            query = query.Where(t => t.Description.Contains(search) || (t.Notes != null && t.Notes.Contains(search)));

        var totalCount = await query.CountAsync();
        const int pageSize = 20;
        var transactions = await query
            .OrderByDescending(t => t.Date)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        ViewBag.Categories = await _db.Categories.Where(c => c.IsSystem || c.UserId == UserId).ToListAsync();
        ViewBag.Accounts = await _db.Accounts.Where(a => a.UserId == UserId && a.IsActive).ToListAsync();
        ViewBag.TotalCount = totalCount;
        ViewBag.Page = page;
        ViewBag.PageSize = pageSize;
        ViewBag.TotalPages = (int)Math.Ceiling((double)totalCount / pageSize);
        ViewBag.CurrentType = type;
        ViewBag.CurrentCategoryId = categoryId;
        ViewBag.CurrentAccountId = accountId;
        ViewBag.From = from?.ToString("yyyy-MM-dd");
        ViewBag.To = to?.ToString("yyyy-MM-dd");
        ViewBag.Search = search;

        return View(transactions);
    }

    public async Task<IActionResult> Create()
    {
        await PopulateDropdowns();
        return View(new TransactionViewModel { Date = DateTime.Today });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(TransactionViewModel vm)
    {
        if (!ModelState.IsValid) { await PopulateDropdowns(); return View(vm); }

        var transaction = new Transaction
        {
            Amount = vm.Amount,
            Description = vm.Description,
            Date = vm.Date,
            Type = vm.Type,
            CategoryId = vm.CategoryId,
            AccountId = vm.AccountId,
            ToAccountId = vm.ToAccountId,
            Notes = vm.Notes,
            Tags = vm.Tags,
            UserId = UserId
        };

        _db.Transactions.Add(transaction);

        // Update account balance
        var account = await _db.Accounts.FindAsync(vm.AccountId);
        if (account != null)
        {
            account.Balance += vm.Type == TransactionType.Income ? vm.Amount :
                               vm.Type == TransactionType.Expense ? -vm.Amount : -vm.Amount;
        }

        if (vm.Type == TransactionType.Transfer && vm.ToAccountId.HasValue)
        {
            var toAccount = await _db.Accounts.FindAsync(vm.ToAccountId.Value);
            if (toAccount != null) toAccount.Balance += vm.Amount;
        }

        await _db.SaveChangesAsync();
        await _financeService.UpdateBudgetSpentAsync(UserId, vm.CategoryId, vm.Date);

        TempData["Success"] = "Transaction added successfully!";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var t = await _db.Transactions.FirstOrDefaultAsync(x => x.Id == id && x.UserId == UserId);
        if (t == null) return NotFound();
        await PopulateDropdowns();
        return View(new TransactionViewModel
        {
            Id = t.Id, Amount = t.Amount, Description = t.Description, Date = t.Date,
            Type = t.Type, CategoryId = t.CategoryId, AccountId = t.AccountId,
            ToAccountId = t.ToAccountId, Notes = t.Notes, Tags = t.Tags
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, TransactionViewModel vm)
    {
        if (!ModelState.IsValid) { await PopulateDropdowns(); return View(vm); }

        var t = await _db.Transactions.FirstOrDefaultAsync(x => x.Id == id && x.UserId == UserId);
        if (t == null) return NotFound();

        // Reverse old balance change
        var oldAccount = await _db.Accounts.FindAsync(t.AccountId);
        if (oldAccount != null)
        {
            oldAccount.Balance -= t.Type == TransactionType.Income ? t.Amount :
                                  t.Type == TransactionType.Expense ? -t.Amount : -t.Amount;
        }

        t.Amount = vm.Amount; t.Description = vm.Description; t.Date = vm.Date;
        t.Type = vm.Type; t.CategoryId = vm.CategoryId; t.AccountId = vm.AccountId;
        t.ToAccountId = vm.ToAccountId; t.Notes = vm.Notes; t.Tags = vm.Tags;

        // Apply new balance change
        var newAccount = await _db.Accounts.FindAsync(vm.AccountId);
        if (newAccount != null)
        {
            newAccount.Balance += vm.Type == TransactionType.Income ? vm.Amount :
                                  vm.Type == TransactionType.Expense ? -vm.Amount : -vm.Amount;
        }

        await _db.SaveChangesAsync();
        TempData["Success"] = "Transaction updated!";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var t = await _db.Transactions.FirstOrDefaultAsync(x => x.Id == id && x.UserId == UserId);
        if (t == null) return NotFound();

        var account = await _db.Accounts.FindAsync(t.AccountId);
        if (account != null)
        {
            account.Balance -= t.Type == TransactionType.Income ? t.Amount :
                               t.Type == TransactionType.Expense ? -t.Amount : -t.Amount;
        }

        _db.Transactions.Remove(t);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Transaction deleted.";
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateDropdowns()
    {
        var categories = await _db.Categories.Where(c => c.IsSystem || c.UserId == UserId).ToListAsync();
        var accounts = await _db.Accounts.Where(a => a.UserId == UserId && a.IsActive).ToListAsync();
        ViewBag.Categories = new SelectList(categories, "Id", "Name");
        ViewBag.CategoriesList = categories;
        ViewBag.Accounts = new SelectList(accounts, "Id", "Name");
        ViewBag.AccountsList = accounts;
    }
}
