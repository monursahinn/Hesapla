using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hesap.Mobile.Models.Api;
using Hesap.Mobile.Services;

namespace Hesap.Mobile.ViewModels;

/// <summary>Oturumdaki kullanıcının yetkisi (yönetici/muhasebe + geçerli abonelik).</summary>
public abstract partial class SessionViewModel : BaseViewModel
{
    protected readonly ApiClient Api;
    protected readonly AuthService Auth;

    protected SessionViewModel(ApiClient api, AuthService auth)
    {
        Api = api;
        Auth = auth;
    }

    public bool CanWrite => Auth.Session?.User.CanWrite == true;

    protected static Task GoAsync(string route, Dictionary<string, object>? query = null) =>
        query == null ? Shell.Current.GoToAsync(route) : Shell.Current.GoToAsync(route, query);

    protected static Task OpenMovementAsync(string type, long? customerId = null, long? invoiceId = null, decimal? amount = null)
    {
        var q = new Dictionary<string, object> { ["type"] = type };
        if (customerId is { } c) q["customerId"] = c;
        if (invoiceId is { } i) q["invoiceId"] = i;
        if (amount is { } a) q["amount"] = a;
        return GoAsync("movement", q);
    }
}

// ============================ PANO ============================

public sealed partial class DashboardViewModel : SessionViewModel
{
    public DashboardViewModel(ApiClient api, AuthService auth) : base(api, auth) { }

    public override IAsyncRelayCommand? AutoLoadCommand => LoadCommand;

    [ObservableProperty]
    public partial Dashboard? Data { get; set; }

    [ObservableProperty]
    public partial string Greeting { get; set; } = "";

    [ObservableProperty]
    public partial string? SubscriptionNotice { get; set; }

    public ObservableCollection<InvoiceListItem> Overdue { get; } = [];
    public ObservableCollection<Account> Accounts { get; } = [];

    [RelayCommand]
    private Task LoadAsync() => RunAsync(async () =>
    {
        var u = Auth.Session?.User;
        Greeting = $"Merhaba, {(u?.FullName ?? "").Split(' ')[0]}";
        SubscriptionNotice = u?.Firm switch
        {
            { Suspended: true } => "Firma hesabınız askıya alındı. Kayıtlar yalnızca görüntülenebilir.",
            { Expired: true } => "Abonelik süreniz doldu. Yeni kayıt için web panelinden aboneliğinizi yenileyin.",
            { DaysLeft: <= 7 } f => $"Aboneliğinizin bitmesine {f.DaysLeft} gün kaldı.",
            _ => null
        };
        OnPropertyChanged(nameof(CanWrite));

        var r = await Api.GetDashboardAsync();
        if (!Check(r) || r.Data == null)
            return;

        Data = r.Data;
        Overdue.Clear();
        foreach (var i in r.Data.OverdueInvoices) Overdue.Add(i);
        Accounts.Clear();
        foreach (var a in r.Data.Accounts) Accounts.Add(a);
    });

    [RelayCommand]
    private Task NewCollectionAsync() => OpenMovementAsync("TA");

    [RelayCommand]
    private Task NewExpenseAsync() => OpenMovementAsync("GI");

    [RelayCommand]
    private Task OpenInvoiceAsync(InvoiceListItem? i) =>
        i == null ? Task.CompletedTask : GoAsync("invoice", new() { ["id"] = i.Id });
}

// ============================ CARİLER ============================

public sealed partial class CustomersViewModel : SessionViewModel
{
    private int _page;
    private int _total;

    public CustomersViewModel(ApiClient api, AuthService auth) : base(api, auth) { }

    public override IAsyncRelayCommand? AutoLoadCommand => HasLoaded ? null : LoadCommand;

    public ObservableCollection<Customer> Items { get; } = [];

    public static IReadOnlyList<Option> Types { get; } = [new("", "Tümü"), new("MU", "Müşteriler"), new("TE", "Tedarikçiler")];

    [ObservableProperty]
    public partial int SelectedTypeIndex { get; set; } = 0;

    private string SelectedTypeValue => Types[Math.Clamp(SelectedTypeIndex, 0, Types.Count - 1)].Value;

    [ObservableProperty]
    public partial string Search { get; set; } = "";

    [ObservableProperty]
    public partial string Summary { get; set; } = "";

    partial void OnSelectedTypeIndexChanged(int value)
    {
        if (HasLoaded) LoadCommand.Execute(null);
    }

    [RelayCommand]
    private Task LoadAsync() => RunAsync(async () =>
    {
        var r = await Api.GetCustomersAsync(Search.Trim(), SelectedTypeValue, 1);
        if (!Check(r) || r.Data == null)
            return;

        _page = 1;
        _total = r.Data.Total;
        Items.Clear();
        foreach (var c in r.Data.Items) Items.Add(c);
        Summary = $"{_total} cari";
        IsEmpty = Items.Count == 0;
    });

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (IsBusy || Items.Count >= _total)
            return;

        IsBusy = true;
        try
        {
            var r = await Api.GetCustomersAsync(Search.Trim(), SelectedTypeValue, _page + 1);
            if (r.IsSuccess && r.Data != null)
            {
                _page++;
                foreach (var c in r.Data.Items) Items.Add(c);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task OpenAsync(Customer? c) => c == null ? Task.CompletedTask : GoAsync("customer", new() { ["id"] = c.Id });
}

public sealed partial class CustomerDetailViewModel : SessionViewModel, IQueryAttributable
{
    private long _id;

    public CustomerDetailViewModel(ApiClient api, AuthService auth) : base(api, auth) { }

    public override IAsyncRelayCommand? AutoLoadCommand => LoadCommand;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var v))
            _id = Convert.ToInt64(v);
    }

    [ObservableProperty]
    public partial Statement? Data { get; set; }

    [ObservableProperty]
    public partial DateTime From { get; set; } = new(DateTime.Today.Year, 1, 1);

    [ObservableProperty]
    public partial DateTime To { get; set; } = DateTime.Today;

    public ObservableCollection<StatementRow> Rows { get; } = [];

    public bool IsCustomer => Data?.Customer.Type != "TE";
    public bool IsSupplier => Data?.Customer.Type != "MU";
    public bool CanCollect => CanWrite && IsCustomer;
    public bool CanPay => CanWrite && IsSupplier;

    [RelayCommand]
    private Task LoadAsync() => RunAsync(async () =>
    {
        if (From > To)
        {
            ErrorMessage = "Başlangıç tarihi bitiş tarihinden sonra olamaz.";
            return;
        }

        var r = await Api.GetStatementAsync(_id, From, To);
        if (!Check(r) || r.Data == null)
            return;

        Data = r.Data;
        Rows.Clear();
        // En yeni hareket üstte.
        foreach (var row in r.Data.Rows.AsEnumerable().Reverse()) Rows.Add(row);
        IsEmpty = Rows.Count == 0;
        OnPropertyChanged(nameof(IsCustomer));
        OnPropertyChanged(nameof(IsSupplier));
        OnPropertyChanged(nameof(CanCollect));
        OnPropertyChanged(nameof(CanPay));
    });

    [RelayCommand]
    private Task CollectAsync() => OpenMovementAsync("TA", _id);

    [RelayCommand]
    private Task PayAsync() => OpenMovementAsync("OD", _id);

    [RelayCommand]
    private Task OpenRowAsync(StatementRow? row) =>
        row is { Kind: "SA" or "AL", RefId: { } id } ? GoAsync("invoice", new() { ["id"] = id }) : Task.CompletedTask;
}

// ============================ FATURALAR ============================

public sealed partial class InvoicesViewModel : SessionViewModel
{
    private int _page;
    private int _total;

    public InvoicesViewModel(ApiClient api, AuthService auth) : base(api, auth) { }

    public override IAsyncRelayCommand? AutoLoadCommand => HasLoaded ? null : LoadCommand;

    public ObservableCollection<InvoiceListItem> Items { get; } = [];

    public static IReadOnlyList<Option> Types { get; } = [new("", "Tümü"), new("SA", "Satış"), new("AL", "Alış")];

    public static IReadOnlyList<Option> Statuses { get; } =
        [new("", "Tüm durumlar"), new("open", "Açık"), new("overdue", "Vadesi geçen"), new("paid", "Ödenen"), new("cancelled", "İptal")];

    [ObservableProperty]
    public partial int SelectedTypeIndex { get; set; } = 0;

    private string SelectedTypeValue => Types[Math.Clamp(SelectedTypeIndex, 0, Types.Count - 1)].Value;

    [ObservableProperty]
    public partial int SelectedStatusIndex { get; set; } = 0;

    private string SelectedStatusValue => Statuses[Math.Clamp(SelectedStatusIndex, 0, Statuses.Count - 1)].Value;

    [ObservableProperty]
    public partial string Search { get; set; } = "";

    [ObservableProperty]
    public partial decimal SumTotal { get; set; }

    [ObservableProperty]
    public partial decimal SumRemaining { get; set; }

    [ObservableProperty]
    public partial int Total { get; set; }

    partial void OnSelectedTypeIndexChanged(int value)
    {
        if (HasLoaded) LoadCommand.Execute(null);
    }

    partial void OnSelectedStatusIndexChanged(int value)
    {
        if (HasLoaded) LoadCommand.Execute(null);
    }

    [RelayCommand]
    private Task LoadAsync() => RunAsync(async () =>
    {
        var r = await Api.GetInvoicesAsync(SelectedTypeValue, SelectedStatusValue, Search.Trim(), null, 1);
        if (!Check(r) || r.Data == null)
            return;

        _page = 1;
        _total = Total = r.Data.Page.Total;
        SumTotal = r.Data.SumTotal;
        SumRemaining = r.Data.SumRemaining;
        Items.Clear();
        foreach (var i in r.Data.Page.Items) Items.Add(i);
        IsEmpty = Items.Count == 0;
    });

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (IsBusy || Items.Count >= _total)
            return;

        IsBusy = true;
        try
        {
            var r = await Api.GetInvoicesAsync(SelectedTypeValue, SelectedStatusValue, Search.Trim(), null, _page + 1);
            if (r.IsSuccess && r.Data != null)
            {
                _page++;
                foreach (var i in r.Data.Page.Items) Items.Add(i);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task OpenAsync(InvoiceListItem? i) => i == null ? Task.CompletedTask : GoAsync("invoice", new() { ["id"] = i.Id });
}

public sealed partial class InvoiceDetailViewModel : SessionViewModel, IQueryAttributable
{
    private long _id;

    public InvoiceDetailViewModel(ApiClient api, AuthService auth) : base(api, auth) { }

    public override IAsyncRelayCommand? AutoLoadCommand => LoadCommand;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var v))
            _id = Convert.ToInt64(v);
    }

    [ObservableProperty]
    public partial Invoice? Data { get; set; }

    [ObservableProperty]
    public partial string Title { get; set; } = "Fatura";

    [ObservableProperty]
    public partial string StatusCode { get; set; } = "";

    public ObservableCollection<InvoiceLine> Lines { get; } = [];
    public ObservableCollection<VatRow> Vats { get; } = [];
    public ObservableCollection<Movement> Payments { get; } = [];

    public bool IsSale => Data?.Type == "SA";
    public string PaymentButtonText => IsSale ? "Tahsilat ekle" : "Ödeme ekle";
    public string PaidLabel => IsSale ? "Tahsil edilen" : "Ödenen";
    public string PaymentsTitle => IsSale ? "Tahsilatlar" : "Ödemeler";
    public bool CanAddPayment => CanWrite && Data is { Cancelled: false, Remaining: > 0 };
    public bool HasDiscount => Data?.DiscountTotal > 0;

    [RelayCommand]
    private Task LoadAsync() => RunAsync(async () =>
    {
        var r = await Api.GetInvoiceAsync(_id);
        if (!Check(r) || r.Data == null)
            return;

        var inv = Data = r.Data;
        Title = inv.DocumentNo;
        StatusCode = inv.Cancelled ? "IP" : inv.Remaining <= 0 ? "OD" : inv.DueDate.Date < DateTime.Today ? "GE" : inv.Paid > 0 ? "KO" : "BE";

        Lines.Clear();
        foreach (var l in inv.Lines) Lines.Add(l);
        Vats.Clear();
        foreach (var v in inv.VatBreakdown) Vats.Add(v);
        Payments.Clear();
        foreach (var p in inv.Payments) Payments.Add(p);

        OnPropertyChanged(nameof(IsSale));
        OnPropertyChanged(nameof(PaymentButtonText));
        OnPropertyChanged(nameof(PaidLabel));
        OnPropertyChanged(nameof(PaymentsTitle));
        OnPropertyChanged(nameof(CanAddPayment));
        OnPropertyChanged(nameof(HasDiscount));
    });

    [RelayCommand]
    private Task AddPaymentAsync() =>
        Data == null ? Task.CompletedTask : OpenMovementAsync(IsSale ? "TA" : "OD", Data.CustomerId, Data.Id, Data.Remaining);

    [RelayCommand]
    private Task OpenCustomerAsync() =>
        Data == null ? Task.CompletedTask : GoAsync("customer", new() { ["id"] = Data.CustomerId });

    /// <summary>PDF'i indirir ve paylaşım menüsüyle açar (e-posta, WhatsApp, yazdır...).</summary>
    [RelayCommand]
    private async Task SharePdfAsync()
    {
        if (Data == null || IsBusy)
            return;

        IsBusy = true;
        try
        {
            var r = await Api.GetInvoicePdfAsync(Data.Id);
            if (!r.IsSuccess || r.Data == null)
            {
                await ToastAsync(r.Message, true);
                return;
            }

            var path = Path.Combine(FileSystem.CacheDirectory, $"{Data.DocumentNo}.pdf");
            await File.WriteAllBytesAsync(path, r.Data);
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = $"Fatura {Data.DocumentNo}",
                File = new ShareFile(path, "application/pdf")
            });
        }
        finally
        {
            IsBusy = false;
        }
    }
}

// ============================ KASA / BANKA ============================

public sealed partial class CashViewModel : SessionViewModel
{
    private int _page;
    private int _total;

    public CashViewModel(ApiClient api, AuthService auth) : base(api, auth) { }

    public override IAsyncRelayCommand? AutoLoadCommand => LoadCommand;

    public ObservableCollection<Account> Accounts { get; } = [];
    public ObservableCollection<Movement> Items { get; } = [];

    public static IReadOnlyList<Option> Types { get; } =
        [new("", "Tümü"), new("TA", "Tahsilat"), new("OD", "Ödeme"), new("GI", "Gider"), new("GE", "Gelir"), new("VI", "Virman")];

    [ObservableProperty]
    public partial int SelectedTypeIndex { get; set; } = 0;

    private string SelectedTypeValue => Types[Math.Clamp(SelectedTypeIndex, 0, Types.Count - 1)].Value;

    partial void OnSelectedTypeIndexChanged(int value)
    {
        if (HasLoaded) LoadCommand.Execute(null);
    }

    [RelayCommand]
    private Task LoadAsync() => RunAsync(async () =>
    {
        OnPropertyChanged(nameof(CanWrite));
        var accounts = Api.GetAccountsAsync();
        var moves = Api.GetMovementsAsync(SelectedTypeValue, null, 1);
        await Task.WhenAll(accounts, moves);

        if (!Check(accounts.Result) || !Check(moves.Result))
            return;

        Accounts.Clear();
        foreach (var a in accounts.Result.Data!.Where(a => a.Active)) Accounts.Add(a);

        _page = 1;
        _total = moves.Result.Data!.Total;
        Items.Clear();
        foreach (var m in moves.Result.Data.Items) Items.Add(m);
        IsEmpty = Items.Count == 0;
    });

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (IsBusy || Items.Count >= _total)
            return;

        IsBusy = true;
        try
        {
            var r = await Api.GetMovementsAsync(SelectedTypeValue, null, _page + 1);
            if (r.IsSuccess && r.Data != null)
            {
                _page++;
                foreach (var m in r.Data.Items) Items.Add(m);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task NewAsync()
    {
        var choice = await Shell.Current.DisplayActionSheetAsync("Yeni kayıt", "Vazgeç", null,
            "Tahsilat", "Ödeme", "Gider", "Gelir", "Virman");

        var type = choice switch
        {
            "Tahsilat" => "TA", "Ödeme" => "OD", "Gider" => "GI", "Gelir" => "GE", "Virman" => "VI", _ => null
        };

        if (type != null)
            await OpenMovementAsync(type);
    }
}

/// <summary>Tahsilat, ödeme, gider, gelir ve virman girişi (web'deki hareket formunun karşılığı).</summary>
public sealed partial class MovementViewModel : SessionViewModel, IQueryAttributable
{
    private long? _presetCustomerId;
    private long? _presetInvoiceId;
    private decimal? _presetAmount;
    private bool _initialized;

    public MovementViewModel(ApiClient api, AuthService auth) : base(api, auth) { }

    public override IAsyncRelayCommand? AutoLoadCommand => _initialized ? null : InitCommand;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("type", out var t)) Type = t as string ?? "TA";
        if (query.TryGetValue("customerId", out var c)) _presetCustomerId = Convert.ToInt64(c);
        if (query.TryGetValue("invoiceId", out var i)) _presetInvoiceId = Convert.ToInt64(i);
        if (query.TryGetValue("amount", out var a)) _presetAmount = Convert.ToDecimal(a);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(NeedsCustomer), nameof(NeedsCategory), nameof(IsTransfer), nameof(AccountLabel))]
    public partial string Type { get; set; } = "TA";

    public string Title => Type switch
    {
        "TA" => "Tahsilat", "OD" => "Ödeme", "GI" => "Gider", "GE" => "Gelir", "VI" => "Virman", _ => "Kayıt"
    };

    public bool NeedsCustomer => Type is "TA" or "OD";
    public bool NeedsCategory => Type is "GI" or "GE";
    public bool IsTransfer => Type == "VI";
    public string AccountLabel => IsTransfer ? "Çıkış hesabı" : "Kasa / banka";

    public ObservableCollection<Account> Accounts { get; } = [];
    public ObservableCollection<Customer> Customers { get; } = [];
    public ObservableCollection<InvoiceListItem> Invoices { get; } = [];
    public ObservableCollection<Category> Categories { get; } = [];

    public static IReadOnlyList<Option> VatRates { get; } = [new("0", "%0"), new("1", "%1"), new("10", "%10"), new("20", "%20")];

    [ObservableProperty]
    public partial DateTime Date { get; set; } = DateTime.Today;

    [ObservableProperty]
    public partial Account? Account { get; set; }

    [ObservableProperty]
    public partial Account? TargetAccount { get; set; }

    [ObservableProperty]
    public partial Customer? Customer { get; set; }

    [ObservableProperty]
    public partial InvoiceListItem? Invoice { get; set; }

    [ObservableProperty]
    public partial Category? Category { get; set; }

    [ObservableProperty]
    public partial int VatRateIndex { get; set; } = 3;

    private string VatRateValue => VatRates[Math.Clamp(VatRateIndex, 0, VatRates.Count - 1)].Value;

    [ObservableProperty]
    public partial string AmountText { get; set; } = "";

    [ObservableProperty]
    public partial string DocumentNo { get; set; } = "";

    [ObservableProperty]
    public partial string Description { get; set; } = "";

    [ObservableProperty]
    public partial bool HasInvoices { get; set; }

    partial void OnCustomerChanged(Customer? value)
    {
        if (_initialized)
            _ = LoadInvoicesAsync(null);
    }

    partial void OnInvoiceChanged(InvoiceListItem? value)
    {
        if (value != null && string.IsNullOrWhiteSpace(AmountText))
            AmountText = value.Remaining.ToString("N2", Tr);
    }

    private static readonly System.Globalization.CultureInfo Tr = new("tr-TR");

    [RelayCommand]
    private Task InitAsync() => RunAsync(async () =>
    {
        var accounts = Api.GetAccountsAsync();
        var customers = NeedsCustomer ? Api.GetCustomersAsync(null, Type == "TA" ? "MU" : "TE", 1, 100) : null;
        var categories = NeedsCategory ? Api.GetCategoriesAsync() : null;
        await Task.WhenAll(new Task[] { accounts, customers ?? Task.CompletedTask, categories ?? Task.CompletedTask });

        if (!Check(accounts.Result))
            return;

        foreach (var a in accounts.Result.Data!.Where(a => a.Active)) Accounts.Add(a);
        Account = Accounts.FirstOrDefault();

        if (customers != null && Check(customers.Result))
        {
            foreach (var c in customers.Result.Data!.Items.Where(c => c.Active || c.Id == _presetCustomerId)) Customers.Add(c);
            Customer = Customers.FirstOrDefault(c => c.Id == _presetCustomerId);
        }

        if (categories != null && Check(categories.Result))
            foreach (var c in categories.Result.Data!.Where(c => c.Active && c.Type == Type)) Categories.Add(c);

        if (_presetAmount is { } amt)
            AmountText = amt.ToString("N2", Tr);

        if (Customer != null)
            await LoadInvoicesAsync(_presetInvoiceId);

        _initialized = true;
    });

    private async Task LoadInvoicesAsync(long? selectId)
    {
        Invoices.Clear();
        Invoice = null;
        HasInvoices = false;

        if (Customer == null)
            return;

        var r = await Api.GetInvoicesAsync(Type == "TA" ? "SA" : "AL", "open", null, Customer.Id, 1, 100);
        if (r.IsSuccess && r.Data != null)
        {
            foreach (var i in r.Data.Page.Items) Invoices.Add(i);
            HasInvoices = Invoices.Count > 0;
            if (selectId != null)
                Invoice = Invoices.FirstOrDefault(i => i.Id == selectId);
        }
    }

    [RelayCommand]
    private void ClearInvoice() => Invoice = null;

    [RelayCommand]
    private Task SaveAsync() => RunAsync(async () =>
    {
        var text = AmountText.Replace(" ", "").Replace("₺", "");
        if (!decimal.TryParse(text, System.Globalization.NumberStyles.Number, Tr, out var amount) || amount <= 0)
        {
            ErrorMessage = "Geçerli bir tutar girin (ör. 1.250,50).";
            return;
        }

        if (Account == null) { ErrorMessage = "Kasa / banka hesabı seçin."; return; }
        if (NeedsCustomer && Customer == null) { ErrorMessage = "Cari seçin."; return; }
        if (NeedsCategory && Category == null) { ErrorMessage = "Kategori seçin."; return; }
        if (IsTransfer && (TargetAccount == null || TargetAccount.Id == Account.Id))
        {
            ErrorMessage = "Farklı bir giriş hesabı seçin.";
            return;
        }

        var r = await Api.CreateMovementAsync(new
        {
            type = Type,
            date = Date.ToString("yyyy-MM-dd"),
            accountId = Account.Id,
            targetAccountId = IsTransfer ? TargetAccount?.Id : null,
            customerId = NeedsCustomer ? Customer?.Id : null,
            invoiceId = NeedsCustomer ? Invoice?.Id : null,
            categoryId = NeedsCategory ? Category?.Id : null,
            amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero),
            vatRate = NeedsCategory ? decimal.Parse(VatRateValue) : 0m,
            documentNo = string.IsNullOrWhiteSpace(DocumentNo) ? null : DocumentNo.Trim(),
            description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
        });

        if (!Check(r))
            return;

        await ToastAsync($"{Title} kaydedildi.");
        await Shell.Current.GoToAsync("..");
    });
}

// ============================ HESABIM ============================

public sealed partial class SettingsViewModel : SessionViewModel
{
    public SettingsViewModel(ApiClient api, AuthService auth) : base(api, auth) { }

    public override IAsyncRelayCommand? AutoLoadCommand => LoadCommand;

    public static IReadOnlyList<Option> Themes { get; } = ThemeService.Options.Select(o => new Option(o.Value, o.Label)).ToList();

    [ObservableProperty]
    public partial Me? User { get; set; }

    [ObservableProperty]
    public partial string PlanText { get; set; } = "";

    [ObservableProperty]
    public partial string SubscriptionText { get; set; } = "";

    [ObservableProperty]
    public partial int ThemeIndex { get; set; } = -1;

    public string Version => $"Sürüm {AppInfo.Current.VersionString} ({AppInfo.Current.BuildString})";

    partial void OnThemeIndexChanged(int value)
    {
        if (value >= 0 && value < Themes.Count && Themes[value].Value != ThemeService.Current)
            ThemeService.Apply(Themes[value].Value);
    }

    [RelayCommand]
    private Task LoadAsync() => RunAsync(async () =>
    {
        ThemeIndex = Themes.ToList().FindIndex(t => t.Value == ThemeService.Current);
        await ((AppShell)Shell.Current).RefreshUserAsync();

        User = Auth.Session?.User;
        var f = User?.Firm;
        PlanText = f?.Plan switch { "DE" => "Deneme", "ST" => "Standart", "PR" => "Profesyonel", var p => p ?? "" };
        SubscriptionText = f == null ? "" :
            f.Suspended ? "Hesap askıya alındı" :
            f.Expired ? $"Süresi doldu ({f.SubscriptionEnd:dd.MM.yyyy})" :
            $"{f.SubscriptionEnd:dd.MM.yyyy} tarihine kadar ({f.DaysLeft} gün)";
    });

    [RelayCommand]
    private Task ChangePasswordAsync() => GoAsync("change-password");
}
