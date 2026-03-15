using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using FinanceTracker.Models;
using FinanceTracker.Services;
using FinanceTracker.ViewModels;
using FinanceTracker.Data;

namespace FinanceTracker.Controllers;

[Authorize]
public class ReportsController : Controller
{
    private readonly FinanceService _financeService;
    private readonly UserManager<ApplicationUser> _userManager;
    private string UserId => _userManager.GetUserId(User)!;

    public ReportsController(FinanceService financeService, UserManager<ApplicationUser> userManager)
    {
        _financeService = financeService; _userManager = userManager;
    }

    public async Task<IActionResult> Index(DateTime? from, DateTime? to)
    {
        from ??= new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
        to ??= DateTime.Today;
        var report = await _financeService.GetReportAsync(UserId, from.Value, to.Value);
        return View(report);
    }
}

public class AccountController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ApplicationDbContext _db;

    public AccountController(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, ApplicationDbContext db)
    {
        _userManager = userManager; _signInManager = signInManager; _db = db;
    }

    public IActionResult Login() => View();

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var result = await _signInManager.PasswordSignInAsync(vm.Email, vm.Password, vm.RememberMe, false);
        if (result.Succeeded) return RedirectToAction("Index", "Home");
        ModelState.AddModelError("", "Invalid login attempt.");
        return View(vm);
    }

    public IActionResult Register() => View();

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var user = new ApplicationUser
        {
            UserName = vm.Email, Email = vm.Email,
            FirstName = vm.FirstName, LastName = vm.LastName
        };
        var result = await _userManager.CreateAsync(user, vm.Password);
        if (result.Succeeded)
        {
            // Create default accounts
            _db.Accounts.AddRange(
                new Account { Name = "Main Checking", Type = AccountType.Checking, Balance = 0, InitialBalance = 0, Color = "#6366f1", Icon = "🏦", UserId = user.Id },
                new Account { Name = "Savings Account", Type = AccountType.Savings, Balance = 0, InitialBalance = 0, Color = "#10b981", Icon = "💰", UserId = user.Id }
            );
            await _db.SaveChangesAsync();
            await _signInManager.SignInAsync(user, false);
            return RedirectToAction("Index", "Home");
        }
        foreach (var e in result.Errors) ModelState.AddModelError("", e.Description);
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }
}
