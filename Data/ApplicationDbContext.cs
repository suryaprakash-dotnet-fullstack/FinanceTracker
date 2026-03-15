using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using FinanceTracker.Models;

namespace FinanceTracker.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<SavingsGoal> SavingsGoals => Set<SavingsGoal>();
    public DbSet<RecurringTransaction> RecurringTransactions => Set<RecurringTransaction>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Transaction>()
            .HasOne(t => t.Account)
            .WithMany(a => a.Transactions)
            .HasForeignKey(t => t.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Transaction>()
            .HasOne(t => t.ToAccount)
            .WithMany(a => a.ToTransactions)
            .HasForeignKey(t => t.ToAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Transaction>()
            .HasOne(t => t.Category)
            .WithMany(c => c.Transactions)
            .HasForeignKey(t => t.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        // Seed default system categories
        builder.Entity<Category>().HasData(
            new Category { Id = 1, Name = "Salary", Icon = "💼", Color = "#10b981", Type = TransactionType.Income, IsSystem = true },
            new Category { Id = 2, Name = "Freelance", Icon = "💻", Color = "#06b6d4", Type = TransactionType.Income, IsSystem = true },
            new Category { Id = 3, Name = "Investment Returns", Icon = "📈", Color = "#8b5cf6", Type = TransactionType.Income, IsSystem = true },
            new Category { Id = 4, Name = "Food & Dining", Icon = "🍔", Color = "#f59e0b", Type = TransactionType.Expense, IsSystem = true },
            new Category { Id = 5, Name = "Transportation", Icon = "🚗", Color = "#ef4444", Type = TransactionType.Expense, IsSystem = true },
            new Category { Id = 6, Name = "Shopping", Icon = "🛍️", Color = "#ec4899", Type = TransactionType.Expense, IsSystem = true },
            new Category { Id = 7, Name = "Housing & Rent", Icon = "🏠", Color = "#6366f1", Type = TransactionType.Expense, IsSystem = true },
            new Category { Id = 8, Name = "Utilities", Icon = "💡", Color = "#f97316", Type = TransactionType.Expense, IsSystem = true },
            new Category { Id = 9, Name = "Healthcare", Icon = "🏥", Color = "#14b8a6", Type = TransactionType.Expense, IsSystem = true },
            new Category { Id = 10, Name = "Entertainment", Icon = "🎬", Color = "#a855f7", Type = TransactionType.Expense, IsSystem = true },
            new Category { Id = 11, Name = "Education", Icon = "📚", Color = "#3b82f6", Type = TransactionType.Expense, IsSystem = true },
            new Category { Id = 12, Name = "Travel", Icon = "✈️", Color = "#06b6d4", Type = TransactionType.Expense, IsSystem = true },
            new Category { Id = 13, Name = "Savings", Icon = "🏦", Color = "#10b981", Type = TransactionType.Expense, IsSystem = true },
            new Category { Id = 14, Name = "Insurance", Icon = "🛡️", Color = "#64748b", Type = TransactionType.Expense, IsSystem = true },
            new Category { Id = 15, Name = "Transfer", Icon = "🔄", Color = "#94a3b8", Type = TransactionType.Transfer, IsSystem = true }
        );
    }
}
