using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FinanceTracker.Data;
using FinanceTracker.Models;
using FinanceTracker.ViewModels;

namespace FinanceTracker.Controllers;

[Authorize]
public class GoalsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private string UserId => _userManager.GetUserId(User)!;

    public GoalsController(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db; _userManager = userManager;
    }

    public async Task<IActionResult> Index()
    {
        var goals = await _db.SavingsGoals.Where(g => g.UserId == UserId).OrderBy(g => g.Status).ToListAsync();
        return View(goals);
    }

    public IActionResult Create() => View(new GoalViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(GoalViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        _db.SavingsGoals.Add(new SavingsGoal
        {
            Name = vm.Name, Description = vm.Description, TargetAmount = vm.TargetAmount,
            CurrentAmount = vm.CurrentAmount, TargetDate = vm.TargetDate,
            Icon = vm.Icon, Color = vm.Color, UserId = UserId
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Goal created!";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddFunds(int id, decimal amount)
    {
        var goal = await _db.SavingsGoals.FirstOrDefaultAsync(g => g.Id == id && g.UserId == UserId);
        if (goal == null) return NotFound();
        goal.CurrentAmount = Math.Min(goal.CurrentAmount + amount, goal.TargetAmount);
        if (goal.CurrentAmount >= goal.TargetAmount) goal.Status = GoalStatus.Completed;
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Added {amount:C} to {goal.Name}!";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var g = await _db.SavingsGoals.FirstOrDefaultAsync(x => x.Id == id && x.UserId == UserId);
        if (g != null) { _db.SavingsGoals.Remove(g); await _db.SaveChangesAsync(); }
        TempData["Success"] = "Goal deleted.";
        return RedirectToAction(nameof(Index));
    }
}

[Authorize]
public class AccountsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private string UserId => _userManager.GetUserId(User)!;

    public AccountsController(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db; _userManager = userManager;
    }

    public async Task<IActionResult> Index()
    {
        var accounts = await _db.Accounts.Where(a => a.UserId == UserId).ToListAsync();
        return View(accounts);
    }

    public IActionResult Create() => View(new AccountViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AccountViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        _db.Accounts.Add(new Account
        {
            Name = vm.Name, Type = vm.Type, Balance = vm.InitialBalance,
            InitialBalance = vm.InitialBalance, Color = vm.Color, Icon = vm.Icon,
            Notes = vm.Notes, UserId = UserId
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Account created!";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(int id)
    {
        var account = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == id && a.UserId == UserId);
        if (account == null) return NotFound();
        var transactions = await _db.Transactions
            .Include(t => t.Category)
            .Where(t => t.AccountId == id && t.UserId == UserId)
            .OrderByDescending(t => t.Date).Take(50).ToListAsync();
        ViewBag.Transactions = transactions;
        return View(account);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var a = await _db.Accounts.FirstOrDefaultAsync(x => x.Id == id && x.UserId == UserId);
        if (a != null) { a.IsActive = false; await _db.SaveChangesAsync(); }
        TempData["Success"] = "Account deactivated.";
        return RedirectToAction(nameof(Index));
    }
}
