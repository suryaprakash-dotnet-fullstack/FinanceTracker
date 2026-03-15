using Microsoft.EntityFrameworkCore;
using FinanceTracker.Data;
using FinanceTracker.Models;
using FinanceTracker.ViewModels;

namespace FinanceTracker.Services;

public class FinanceService
{
    private readonly ApplicationDbContext _db;

    public FinanceService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<DashboardViewModel> GetDashboardAsync(string userId)
    {
        var now = DateTime.Now;
        var monthStart = new DateTime(now.Year, now.Month, 1);
        var monthEnd = monthStart.AddMonths(1);

        var accounts = await _db.Accounts.Where(a => a.UserId == userId && a.IsActive).ToListAsync();
        var totalBalance = accounts.Sum(a => a.Balance);

        var monthTransactions = await _db.Transactions
            .Include(t => t.Category)
            .Where(t => t.UserId == userId && t.Date >= monthStart && t.Date < monthEnd)
            .ToListAsync();

        var monthlyIncome = monthTransactions.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount);
        var monthlyExpenses = monthTransactions.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount);

        var recentTransactions = await _db.Transactions
            .Include(t => t.Category)
            .Include(t => t.Account)
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.Date)
            .Take(10)
            .ToListAsync();

        var budgets = await _db.Budgets
            .Include(b => b.Category)
            .Where(b => b.UserId == userId && b.Month == now.Month && b.Year == now.Year)
            .ToListAsync();

        // Update budget spent amounts
        foreach (var budget in budgets)
        {
            budget.Spent = monthTransactions
                .Where(t => t.CategoryId == budget.CategoryId && t.Type == TransactionType.Expense)
                .Sum(t => t.Amount);
        }

        var goals = await _db.SavingsGoals
            .Where(g => g.UserId == userId && g.Status == GoalStatus.Active)
            .Take(4)
            .ToListAsync();

        var expensesByCategory = monthTransactions
            .Where(t => t.Type == TransactionType.Expense && t.Category != null)
            .GroupBy(t => t.Category!.Name)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));

        var last6Months = new List<MonthlyData>();
        for (int i = 5; i >= 0; i--)
        {
            var d = now.AddMonths(-i);
            var start = new DateTime(d.Year, d.Month, 1);
            var end = start.AddMonths(1);
            var txns = await _db.Transactions
                .Where(t => t.UserId == userId && t.Date >= start && t.Date < end)
                .ToListAsync();
            last6Months.Add(new MonthlyData
            {
                Month = d.ToString("MMM yyyy"),
                Income = txns.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount),
                Expenses = txns.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount)
            });
        }

        return new DashboardViewModel
        {
            TotalBalance = totalBalance,
            MonthlyIncome = monthlyIncome,
            MonthlyExpenses = monthlyExpenses,
            Accounts = accounts,
            RecentTransactions = recentTransactions,
            ActiveBudgets = budgets,
            ActiveGoals = goals,
            ExpensesByCategory = expensesByCategory,
            Last6MonthsData = last6Months,
            TransactionCount = await _db.Transactions.CountAsync(t => t.UserId == userId)
        };
    }

    public async Task<ReportViewModel> GetReportAsync(string userId, DateTime start, DateTime end)
    {
        var end2 = end.AddDays(1);
        var transactions = await _db.Transactions
            .Include(t => t.Category)
            .Include(t => t.Account)
            .Where(t => t.UserId == userId && t.Date >= start && t.Date < end2)
            .OrderByDescending(t => t.Date)
            .ToListAsync();

        var totalIncome = transactions.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount);
        var totalExpenses = transactions.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount);

        var expensesByCategory = transactions
            .Where(t => t.Type == TransactionType.Expense && t.Category != null)
            .GroupBy(t => t.Category!)
            .Select(g => new CategoryReport
            {
                CategoryName = g.Key.Name,
                Color = g.Key.Color,
                Icon = g.Key.Icon,
                Amount = g.Sum(t => t.Amount),
                Count = g.Count(),
                Percentage = totalExpenses > 0 ? (g.Sum(t => t.Amount) / totalExpenses) * 100 : 0
            })
            .OrderByDescending(c => c.Amount)
            .ToList();

        var incomeByCategory = transactions
            .Where(t => t.Type == TransactionType.Income && t.Category != null)
            .GroupBy(t => t.Category!)
            .Select(g => new CategoryReport
            {
                CategoryName = g.Key.Name,
                Color = g.Key.Color,
                Icon = g.Key.Icon,
                Amount = g.Sum(t => t.Amount),
                Count = g.Count(),
                Percentage = totalIncome > 0 ? (g.Sum(t => t.Amount) / totalIncome) * 100 : 0
            })
            .OrderByDescending(c => c.Amount)
            .ToList();

        // Monthly trend
        var monthlyTrend = transactions
            .GroupBy(t => new { t.Date.Year, t.Date.Month })
            .Select(g => new MonthlyData
            {
                Month = new DateTime(g.Key.Year, g.Key.Month, 1).ToString("MMM yyyy"),
                Income = g.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount),
                Expenses = g.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount)
            })
            .OrderBy(m => m.Month)
            .ToList();

        return new ReportViewModel
        {
            StartDate = start,
            EndDate = end,
            TotalIncome = totalIncome,
            TotalExpenses = totalExpenses,
            ExpensesByCategory = expensesByCategory,
            IncomeByCategory = incomeByCategory,
            MonthlyTrend = monthlyTrend,
            Transactions = transactions
        };
    }

    public async Task UpdateBudgetSpentAsync(string userId, int categoryId, DateTime date)
    {
        var budget = await _db.Budgets
            .FirstOrDefaultAsync(b => b.UserId == userId && b.CategoryId == categoryId
                && b.Month == date.Month && b.Year == date.Year);
        if (budget != null)
        {
            var monthStart = new DateTime(date.Year, date.Month, 1);
            var monthEnd = monthStart.AddMonths(1);
            budget.Spent = await _db.Transactions
                .Where(t => t.UserId == userId && t.CategoryId == categoryId
                    && t.Type == TransactionType.Expense
                    && t.Date >= monthStart && t.Date < monthEnd)
                .SumAsync(t => t.Amount);
            await _db.SaveChangesAsync();
        }
    }
}
