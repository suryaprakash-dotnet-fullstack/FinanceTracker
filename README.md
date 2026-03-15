# 💸 FinanceTracker - Personal Finance Management App

A feature-rich **ASP.NET Core 8 MVC** personal finance tracker with a modern UI.

## 🚀 Features

### 💰 Dashboard
- Real-time financial overview (balance, income, expenses, net savings)
- Income vs Expenses bar chart (last 6 months)
- Expense breakdown by category (donut chart)
- Recent transactions list
- Active budgets with progress bars
- Savings goals tracker

### 📊 Transactions
- Add/Edit/Delete income, expense, and transfer transactions
- Filter by type, category, account, date range, search
- Pagination (20 per page)
- Tags support for transactions
- Notes field for extra details
- Automatic account balance updates

### 🏦 Accounts
- Multiple account types: Checking, Savings, Credit Card, Investment, Cash, Loan
- Custom icons & colors
- Account-level transaction history
- Net worth calculation

### 🎯 Budgets
- Monthly budget planning per category
- Visual progress bars (green/yellow/red)
- Budget vs Actual spending comparison
- Month-by-month navigation
- Rollover budget option

### 💪 Savings Goals
- Create savings goals with target amounts and dates
- Track progress with visual progress bars
- Add funds to goals
- Goal completion tracking

### 📈 Reports & Analytics
- Custom date range reports
- Expense breakdown by category (with percentages)
- Income source breakdown
- Monthly trend line chart
- Full transaction list for the period
- Quick filters: This Month, This Year, Last 3 Months

### 🔐 Authentication
- User registration with email/password
- Secure login with Remember Me option
- Data isolation per user (all data is user-specific)
- Default accounts created on registration

### 📋 Default Categories (15 built-in)
Income: Salary, Freelance, Investment Returns  
Expense: Food, Transport, Shopping, Housing, Utilities, Healthcare, Entertainment, Education, Travel, Savings, Insurance  
Transfer: Transfer

---

## 🛠️ Tech Stack
- **Framework**: ASP.NET Core 8 MVC
- **ORM**: Entity Framework Core 8
- **Database**: SQLite (file-based, zero config)
- **Auth**: ASP.NET Core Identity
- **Charts**: Chart.js
- **Icons**: Font Awesome 6
- **Fonts**: Google Fonts (Inter)
- **Styling**: Custom CSS (no Bootstrap dependency)

---

## ⚡ Getting Started

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Run the App

```bash
# 1. Navigate to project folder
cd FinanceTracker

# 2. Apply database migrations (creates SQLite DB automatically)
dotnet ef database update

# 3. Run the application
dotnet run
```

The app will start at **https://localhost:5001** or **http://localhost:5000**

### First Time Setup
1. Open the app in your browser
2. Click **"Create one free"** to register
3. Enter your name, email, and password
4. Two default accounts (Checking + Savings) are auto-created
5. Start adding transactions!

---

## 📁 Project Structure

```
FinanceTracker/
├── Controllers/
│   ├── HomeController.cs          # Dashboard
│   ├── TransactionsController.cs  # CRUD transactions
│   ├── BudgetsController.cs       # Budget management
│   ├── GoalsAccountsController.cs # Goals + Accounts
│   └── ReportsAccountController.cs # Reports + Auth
├── Data/
│   └── ApplicationDbContext.cs    # EF Core DbContext + seed data
├── Models/
│   ├── ApplicationUser.cs         # Extended Identity user
│   └── Models.cs                  # All domain models
├── Services/
│   └── FinanceService.cs          # Business logic
├── ViewModels/
│   └── ViewModels.cs              # All ViewModels
├── Views/
│   ├── Shared/_Layout.cshtml      # Main layout with sidebar
│   ├── Home/Index.cshtml          # Dashboard
│   ├── Transactions/              # Transaction views
│   ├── Budgets/                   # Budget views
│   ├── Goals/                     # Savings goal views
│   ├── Accounts/                  # Account views
│   ├── Reports/                   # Analytics views
│   └── Account/                   # Login/Register
├── Program.cs                     # App configuration
└── FinanceTracker.csproj          # Project file
```

---

## 🔧 Customization

### Change Currency
Edit `Program.cs` or add a user preference - currently uses USD (`ToString("C")`).  
To use INR: replace `ToString("C")` with `ToString("C", new System.Globalization.CultureInfo("en-IN"))` in the views.

### Add New Categories
Categories are seeded in `ApplicationDbContext.cs` in the `OnModelCreating` method. Add more there or use the custom category feature via the database.

### Change Database
To switch from SQLite to SQL Server, update `Program.cs`:
```csharp
// Replace SQLite with SQL Server
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
```
And update the NuGet package from `Microsoft.EntityFrameworkCore.Sqlite` to `Microsoft.EntityFrameworkCore.SqlServer`.

---

## 📦 NuGet Packages Used
- `Microsoft.EntityFrameworkCore.Sqlite` 8.0.0
- `Microsoft.EntityFrameworkCore.Tools` 8.0.0
- `Microsoft.AspNetCore.Identity.EntityFrameworkCore` 8.0.0
- `Microsoft.AspNetCore.Identity.UI` 8.0.0

---

## 🌟 Future Enhancements (Ideas)
- CSV/Excel export of transactions
- Recurring transaction automation
- Email notifications for budget alerts
- Dark mode toggle
- Currency conversion support
- Bill reminders / due dates
- Financial calculators (EMI, SIP, FD)
- WhatsApp/SMS notifications

---

Built with ❤️ using ASP.NET Core 8 MVC
