using System.ComponentModel.DataAnnotations;
using FinanceTracker.Models;

namespace FinanceTracker.ViewModels;

public class DashboardViewModel
{
    public decimal TotalBalance { get; set; }
    public decimal MonthlyIncome { get; set; }
    public decimal MonthlyExpenses { get; set; }
    public decimal MonthlySavings => MonthlyIncome - MonthlyExpenses;
    public List<Account> Accounts { get; set; } = new();
    public List<Transaction> RecentTransactions { get; set; } = new();
    public List<Budget> ActiveBudgets { get; set; } = new();
    public List<SavingsGoal> ActiveGoals { get; set; } = new();
    public Dictionary<string, decimal> ExpensesByCategory { get; set; } = new();
    public List<MonthlyData> Last6MonthsData { get; set; } = new();
    public int TransactionCount { get; set; }
}

public class MonthlyData
{
    public string Month { get; set; } = string.Empty;
    public decimal Income { get; set; }
    public decimal Expenses { get; set; }
}

public class TransactionViewModel
{
    public int Id { get; set; }
    [Required]
    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be positive")]
    public decimal Amount { get; set; }
    [Required, MaxLength(500)]
    public string Description { get; set; } = string.Empty;
    [Required]
    public DateTime Date { get; set; } = DateTime.Today;
    [Required]
    public TransactionType Type { get; set; }
    [Required]
    public int CategoryId { get; set; }
    [Required]
    public int AccountId { get; set; }
    public int? ToAccountId { get; set; }
    public string? Notes { get; set; }
    public string? Tags { get; set; }
}

public class BudgetViewModel
{
    public int Id { get; set; }
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;
    [Required, Range(0.01, double.MaxValue)]
    public decimal Amount { get; set; }
    [Required]
    public int CategoryId { get; set; }
    public int Month { get; set; } = DateTime.Now.Month;
    public int Year { get; set; } = DateTime.Now.Year;
    public bool RolloverEnabled { get; set; }
    public string? Notes { get; set; }
}

public class GoalViewModel
{
    public int Id { get; set; }
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;
    [MaxLength(500)]
    public string? Description { get; set; }
    [Required, Range(0.01, double.MaxValue)]
    public decimal TargetAmount { get; set; }
    public decimal CurrentAmount { get; set; }
    public DateTime? TargetDate { get; set; }
    public string Icon { get; set; } = "🎯";
    public string Color { get; set; } = "#10b981";
}

public class AccountViewModel
{
    public int Id { get; set; }
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;
    [Required]
    public AccountType Type { get; set; }
    public decimal InitialBalance { get; set; }
    public string Color { get; set; } = "#6366f1";
    public string Icon { get; set; } = "🏦";
    public string? Notes { get; set; }
}

public class ReportViewModel
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public decimal TotalIncome { get; set; }
    public decimal TotalExpenses { get; set; }
    public decimal NetSavings => TotalIncome - TotalExpenses;
    public List<CategoryReport> ExpensesByCategory { get; set; } = new();
    public List<CategoryReport> IncomeByCategory { get; set; } = new();
    public List<MonthlyData> MonthlyTrend { get; set; } = new();
    public List<Transaction> Transactions { get; set; } = new();
}

public class CategoryReport
{
    public string CategoryName { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int Count { get; set; }
    public decimal Percentage { get; set; }
}

public class RegisterViewModel
{
    [Required]
    public string FirstName { get; set; } = string.Empty;
    [Required]
    public string LastName { get; set; } = string.Empty;
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;
    [Required, MinLength(6)]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;
    [Compare("Password")]
    [DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class LoginViewModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;
    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;
    public bool RememberMe { get; set; }
}
