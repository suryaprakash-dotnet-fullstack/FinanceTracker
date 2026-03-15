using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FinanceTracker.Models;

public enum TransactionType { Income, Expense, Transfer }
public enum RecurrenceFrequency { Daily, Weekly, Monthly, Yearly }
public enum AccountType { Checking, Savings, CreditCard, Investment, Cash, Loan }
public enum GoalStatus { Active, Completed, Paused }

public class Category
{
    public int Id { get; set; }
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = "💰";
    public string Color { get; set; } = "#6366f1";
    public TransactionType Type { get; set; }
    public bool IsSystem { get; set; } = false;
    public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }
    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
    public ICollection<Budget> Budgets { get; set; } = new List<Budget>();
}

public class Account
{
    public int Id { get; set; }
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;
    public AccountType Type { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public decimal Balance { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public decimal InitialBalance { get; set; }
    public string Color { get; set; } = "#6366f1";
    public string Icon { get; set; } = "🏦";
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }
    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
    public ICollection<Transaction> ToTransactions { get; set; } = new List<Transaction>();
}

public class Transaction
{
    public int Id { get; set; }
    [Required]
    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }
    [Required, MaxLength(500)]
    public string Description { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public TransactionType Type { get; set; }
    public int CategoryId { get; set; }
    public Category? Category { get; set; }
    public int AccountId { get; set; }
    public Account? Account { get; set; }
    public int? ToAccountId { get; set; }
    public Account? ToAccount { get; set; }
    public string? Notes { get; set; }
    public string? Tags { get; set; }
    public bool IsRecurring { get; set; }
    public int? RecurringTransactionId { get; set; }
    public RecurringTransaction? RecurringTransaction { get; set; }
    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Budget
{
    public int Id { get; set; }
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;
    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public decimal Spent { get; set; }
    public int CategoryId { get; set; }
    public Category? Category { get; set; }
    public int Month { get; set; }
    public int Year { get; set; }
    public bool RolloverEnabled { get; set; }
    public string? Notes { get; set; }
    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }
    [NotMapped]
    public decimal Remaining => Amount - Spent;
    [NotMapped]
    public decimal PercentUsed => Amount > 0 ? (Spent / Amount) * 100 : 0;
}

public class SavingsGoal
{
    public int Id { get; set; }
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;
    [MaxLength(500)]
    public string? Description { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public decimal TargetAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public decimal CurrentAmount { get; set; }
    public DateTime? TargetDate { get; set; }
    public GoalStatus Status { get; set; } = GoalStatus.Active;
    public string Icon { get; set; } = "🎯";
    public string Color { get; set; } = "#10b981";
    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [NotMapped]
    public decimal Progress => TargetAmount > 0 ? Math.Min((CurrentAmount / TargetAmount) * 100, 100) : 0;
    [NotMapped]
    public decimal Remaining => Math.Max(TargetAmount - CurrentAmount, 0);
}

public class RecurringTransaction
{
    public int Id { get; set; }
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;
    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }
    public TransactionType Type { get; set; }
    public int CategoryId { get; set; }
    public Category? Category { get; set; }
    public int AccountId { get; set; }
    public Account? Account { get; set; }
    public RecurrenceFrequency Frequency { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public DateTime? NextDueDate { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }
}
