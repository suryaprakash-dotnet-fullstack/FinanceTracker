using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using FinanceTracker.Models;
using FinanceTracker.Services;

namespace FinanceTracker.Controllers;

[Authorize]
public class HomeController : Controller
{
    private readonly FinanceService _financeService;
    private readonly UserManager<ApplicationUser> _userManager;

    public HomeController(FinanceService financeService, UserManager<ApplicationUser> userManager)
    {
        _financeService = financeService;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index()
    {
        var userId = _userManager.GetUserId(User)!;
        var vm = await _financeService.GetDashboardAsync(userId);
        return View(vm);
    }
}
