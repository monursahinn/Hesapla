namespace Hesapla.Mobile.Models.Api;

/// <summary>Sunucu yanıt zarfı: { success, message, data }.</summary>
public sealed class ApiResponse<T>
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public T? Data { get; set; }
}

public sealed class FirmSummary
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Plan { get; set; } = "";
    public DateTime SubscriptionEnd { get; set; }
    public int DaysLeft { get; set; }
    public bool Expired { get; set; }
    public bool Suspended { get; set; }
}

public sealed class Me
{
    public long UserId { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "";
    public string RoleLabel { get; set; } = "";
    public FirmSummary? Firm { get; set; }
    public string AppName { get; set; } = "Hesapla";

    /// <summary>Kayıt girebilir mi (yönetici/muhasebe ve abonelik geçerli).</summary>
    public bool CanWrite => Role is "AD" or "AC" && Firm is { Expired: false, Suspended: false };
}

/// <summary>Mobil oturum: kısa ömürlü erişim belirteci + 30 günlük tek kullanımlık yenileme belirteci.</summary>
public sealed class MobileSession
{
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public int ExpiresIn { get; set; }
    public Me User { get; set; } = new();
    public DateTime AccessExpiresAtUtc { get; set; }
    public DateTime RefreshExpiresAtUtc { get; set; }
}

public sealed class Paged<T>
{
    public List<T> Items { get; set; } = [];
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public sealed class Customer
{
    public long Id { get; set; }
    public string Type { get; set; } = "";
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string? TaxNumber { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? City { get; set; }
    public string? Address { get; set; }
    public int PaymentTermDays { get; set; }
    public bool Active { get; set; }
    public decimal Balance { get; set; }

    public string TypeText => Type switch { "MU" => "Müşteri", "TE" => "Tedarikçi", _ => "Müşteri ve tedarikçi" };
    public string BalanceText => Balance == 0 ? "Bakiye yok" : Balance > 0 ? "Alacak" : "Borç";
    public decimal AbsBalance => Math.Abs(Balance);
}

public sealed class StatementRow
{
    public DateTime Date { get; set; }
    public string Kind { get; set; } = "";
    public string KindLabel { get; set; } = "";
    public string? DocumentNo { get; set; }
    public string? Description { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal Balance { get; set; }
    public long? RefId { get; set; }

    public decimal Amount => Debit > 0 ? Debit : Credit;
    public bool IsDebit => Debit > 0;
}

public sealed class Statement
{
    public Customer Customer { get; set; } = new();
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public decimal OpeningBalance { get; set; }
    public List<StatementRow> Rows { get; set; } = [];
    public decimal ClosingBalance { get; set; }
}

public sealed class InvoiceListItem
{
    public long Id { get; set; }
    public string Type { get; set; } = "";
    public string DocumentNo { get; set; } = "";
    public long CustomerId { get; set; }
    public string CustomerTitle { get; set; } = "";
    public DateTime Date { get; set; }
    public DateTime DueDate { get; set; }
    public decimal Total { get; set; }
    public decimal Paid { get; set; }
    public decimal Remaining { get; set; }
    public bool Cancelled { get; set; }
    public string Status { get; set; } = "";
    public string StatusLabel { get; set; } = "";

    public string TypeText => Type == "SA" ? "Satış" : "Alış";
    public string PickerText => $"{DocumentNo} • kalan {Remaining:N2} ₺";
}

public sealed class InvoiceListResult
{
    public Paged<InvoiceListItem> Page { get; set; } = new();
    public decimal SumTotal { get; set; }
    public decimal SumRemaining { get; set; }
}

public sealed class InvoiceLine
{
    public int Order { get; set; }
    public string Description { get; set; } = "";
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public decimal DiscountRate { get; set; }
    public decimal VatRate { get; set; }
    public decimal Amount { get; set; }
    public decimal VatAmount { get; set; }

    public string QuantityText => $"{Quantity:0.###} {Unit} × {UnitPrice:N2} ₺{(DiscountRate > 0 ? $" (−%{DiscountRate:0.##})" : "")} • KDV %{VatRate:0.##}";
}

public sealed class Movement
{
    public long Id { get; set; }
    public string Type { get; set; } = "";
    public string TypeLabel { get; set; } = "";
    public DateTime Date { get; set; }
    public string AccountName { get; set; } = "";
    public string? TargetAccountName { get; set; }
    public string? CustomerTitle { get; set; }
    public string? InvoiceNo { get; set; }
    public string? CategoryName { get; set; }
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public bool Cancelled { get; set; }

    public string DateLine => Date.ToString("dd.MM.yyyy") + (Cancelled ? " • iptal edildi" : "");
    public bool Incoming => Type is "TA" or "GE";
    public string Title => CustomerTitle ?? CategoryName ?? (Type == "VI" ? $"{AccountName} → {TargetAccountName}" : TypeLabel);
    public string Subtitle => $"{TypeLabel}{(Cancelled ? " (iptal)" : "")} • {AccountName}{(InvoiceNo != null ? $" • {InvoiceNo}" : "")}";
    public string SignedAmount => $"{(Type == "VI" ? "" : Incoming ? "+" : "−")}{Amount:N2} ₺";
}

public sealed class VatRow
{
    public decimal Rate { get; set; }
    public decimal BaseAmount { get; set; }
    public decimal Vat { get; set; }
}

public sealed class Invoice
{
    public long Id { get; set; }
    public string Type { get; set; } = "";
    public string DocumentNo { get; set; } = "";
    public long CustomerId { get; set; }
    public string CustomerTitle { get; set; } = "";
    public DateTime Date { get; set; }
    public DateTime DueDate { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal Total { get; set; }
    public decimal Paid { get; set; }
    public decimal Remaining { get; set; }
    public string? Description { get; set; }
    public bool Cancelled { get; set; }
    public string? CancelReason { get; set; }
    public List<InvoiceLine> Lines { get; set; } = [];
    public List<Movement> Payments { get; set; } = [];
    public List<VatRow> VatBreakdown { get; set; } = [];

    public bool IsSettled => Remaining <= 0;
}

public sealed class Account
{
    public long Id { get; set; }
    public string Type { get; set; } = "";
    public string Name { get; set; } = "";
    public string? BankName { get; set; }
    public bool Active { get; set; }
    public decimal Balance { get; set; }

    public string Display => $"{Name} — {Balance:N2} ₺";
    public string TypeText => Type == "KA" ? "Kasa" : BankName ?? "Banka";
}

public sealed class Category
{
    public long Id { get; set; }
    public string Type { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Active { get; set; }
}

public sealed class Dashboard
{
    public decimal CashTotal { get; set; }
    public decimal BankTotal { get; set; }
    public decimal Receivables { get; set; }
    public decimal Payables { get; set; }
    public decimal MonthSales { get; set; }
    public decimal MonthPurchases { get; set; }
    public decimal MonthExpenses { get; set; }
    public decimal MonthCollections { get; set; }
    public decimal OverdueReceivables { get; set; }
    public bool HasOverdue => OverdueReceivables > 0;
    public List<InvoiceListItem> OverdueInvoices { get; set; } = [];
    public List<InvoiceListItem> UpcomingPayables { get; set; } = [];
    public List<Account> Accounts { get; set; } = [];
}

/// <summary>Basit seçim öğesi (Picker için).</summary>
public sealed record Option(string Value, string Text)
{
    public override string ToString() => Text;
}
