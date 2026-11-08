using ClosedXML.Excel;
using Hesap.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Hesap.Features;

/// <summary>Fatura PDF'i (QuestPDF). e-Fatura değildir; firmanın kendi kaydı ve müşteriye gönderilen proforma/bilgi nüshasıdır.</summary>
public sealed class InvoicePdf(InvoiceService invoices, CompanyService company)
{
    static InvoicePdf()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        // Yazı tipinde olmayan karakterler (emoji vb.) PDF'in tamamını düşürmesin.
        QuestPDF.Settings.ThrowOnMissingTextGlyphs = false;
    }

    private static string M(decimal v) => v.ToString("N2", Money.Tr);

    public async Task<(byte[] Bytes, string FileName)> RenderAsync(long firmId, long invoiceId)
    {
        var inv = await invoices.GetAsync(firmId, invoiceId);
        var firm = await company.GetFirmAsync(firmId);
        var logoPath = await company.GetLogoPathAsync(firmId);
        var logo = logoPath == null ? null : await File.ReadAllBytesAsync(logoPath);

        var title = inv.Type == "SA" ? "SATIŞ FATURASI" : "ALIŞ FATURASI";
        var (seller, buyer) = inv.Type == "SA"
            ? ((Name: firm.Name, Tax: firm.TaxNumber, Office: firm.TaxOffice, Address: string.Join(" ", new[] { firm.Address, firm.District, firm.City }.Where(x => !string.IsNullOrWhiteSpace(x)))),
               (Name: inv.CustomerTitle, Tax: inv.CustomerTaxNumber, Office: inv.CustomerTaxOffice, Address: inv.CustomerAddress))
            : ((Name: inv.CustomerTitle, Tax: inv.CustomerTaxNumber, Office: inv.CustomerTaxOffice, Address: inv.CustomerAddress),
               (Name: firm.Name, Tax: firm.TaxNumber, Office: firm.TaxOffice, Address: string.Join(" ", new[] { firm.Address, firm.District, firm.City }.Where(x => !string.IsNullOrWhiteSpace(x)))));

        var doc = Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(32);
            page.DefaultTextStyle(x => x.FontSize(9).FontColor(Colors.Grey.Darken4));

            page.Header().Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    if (logo != null)
                        col.Item().Height(48).AlignLeft().Image(logo).FitHeight();
                    col.Item().PaddingTop(4).Text(seller.Name).Bold().FontSize(12);
                    if (!string.IsNullOrWhiteSpace(seller.Address)) col.Item().Text(seller.Address);
                    if (!string.IsNullOrWhiteSpace(seller.Tax)) col.Item().Text($"VKN/TCKN: {seller.Tax}{(string.IsNullOrWhiteSpace(seller.Office) ? "" : $" • {seller.Office} V.D.")}");
                    if (inv.Type == "SA" && !string.IsNullOrWhiteSpace(firm.Phone)) col.Item().Text($"Tel: {firm.Phone}");
                });
                row.ConstantItem(200).AlignRight().Column(col =>
                {
                    col.Item().AlignRight().Text(title).Bold().FontSize(16).FontColor(inv.Cancelled ? Colors.Red.Medium : Colors.Teal.Darken2);
                    col.Item().AlignRight().Text($"No: {inv.DocumentNo}").SemiBold();
                    col.Item().AlignRight().Text($"Tarih: {inv.Date:dd.MM.yyyy}");
                    col.Item().AlignRight().Text($"Vade: {inv.DueDate:dd.MM.yyyy}");
                    if (inv.Cancelled) col.Item().AlignRight().Text("İPTAL EDİLDİ").Bold().FontColor(Colors.Red.Medium);
                });
            });

            page.Content().PaddingVertical(16).Column(col =>
            {
                col.Item().Background(Colors.Grey.Lighten4).Padding(10).Column(b =>
                {
                    b.Item().Text(inv.Type == "SA" ? "Sayın" : "Tedarikçi").FontSize(8).FontColor(Colors.Grey.Darken1);
                    b.Item().Text(inv.Type == "SA" ? buyer.Name : seller.Name).Bold().FontSize(11);
                    var party = inv.Type == "SA" ? buyer : seller;
                    if (!string.IsNullOrWhiteSpace(party.Address)) b.Item().Text(party.Address);
                    if (!string.IsNullOrWhiteSpace(party.Tax)) b.Item().Text($"VKN/TCKN: {party.Tax}{(string.IsNullOrWhiteSpace(party.Office) ? "" : $" • {party.Office} V.D.")}");
                });

                col.Item().PaddingTop(14).Table(t =>
                {
                    t.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(22); c.RelativeColumn(5); c.RelativeColumn(1.4f); c.RelativeColumn(1.8f);
                        c.RelativeColumn(1.1f); c.RelativeColumn(1); c.RelativeColumn(2);
                    });

                    static IContainer Head(IContainer c) => c.BorderBottom(1).BorderColor(Colors.Grey.Medium).PaddingVertical(4);
                    t.Header(h =>
                    {
                        h.Cell().Element(Head).Text("#").SemiBold();
                        h.Cell().Element(Head).Text("Açıklama").SemiBold();
                        h.Cell().Element(Head).AlignRight().Text("Miktar").SemiBold();
                        h.Cell().Element(Head).AlignRight().Text("Birim fiyat").SemiBold();
                        h.Cell().Element(Head).AlignRight().Text("İsk. %").SemiBold();
                        h.Cell().Element(Head).AlignRight().Text("KDV %").SemiBold();
                        h.Cell().Element(Head).AlignRight().Text("Tutar").SemiBold();
                    });

                    static IContainer Cell(IContainer c) => c.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(4);
                    foreach (var l in inv.Lines)
                    {
                        t.Cell().Element(Cell).Text(l.Order.ToString());
                        t.Cell().Element(Cell).Text(l.Description);
                        t.Cell().Element(Cell).AlignRight().Text($"{l.Quantity.ToString("0.###", Money.Tr)} {l.Unit}");
                        t.Cell().Element(Cell).AlignRight().Text(l.UnitPrice.ToString("N2", Money.Tr));
                        t.Cell().Element(Cell).AlignRight().Text(l.DiscountRate == 0 ? "-" : l.DiscountRate.ToString("0.##", Money.Tr));
                        t.Cell().Element(Cell).AlignRight().Text(l.VatRate.ToString("0.##", Money.Tr));
                        t.Cell().Element(Cell).AlignRight().Text(M(l.Amount));
                    }
                });

                col.Item().PaddingTop(12).AlignRight().Width(240).Column(tot =>
                {
                    void Line(string label, string value, bool bold = false) => tot.Item().Row(r =>
                    {
                        var left = r.RelativeItem().Text(label);
                        var right = r.ConstantItem(110).AlignRight().Text(value);
                        if (bold) { left.Bold(); right.Bold(); }
                    });

                    if (inv.DiscountTotal > 0) Line("Toplam iskonto", $"{M(inv.DiscountTotal)} ₺");
                    Line("Ara toplam (KDV hariç)", $"{M(inv.Subtotal)} ₺");
                    foreach (var v in inv.Lines.GroupBy(x => x.VatRate).OrderBy(g => g.Key))
                        Line($"KDV %{v.Key.ToString("0.##", Money.Tr)}", $"{M(v.Sum(x => x.VatAmount))} ₺");
                    tot.Item().PaddingTop(4).BorderTop(1).BorderColor(Colors.Grey.Medium).PaddingTop(4).Row(r =>
                    {
                        r.RelativeItem().Text("GENEL TOPLAM").Bold().FontSize(11);
                        r.ConstantItem(110).AlignRight().Text($"{M(inv.Total)} ₺").Bold().FontSize(11);
                    });
                    if (inv.Paid > 0 && !inv.Cancelled)
                    {
                        Line("Ödenen", $"{M(inv.Paid)} ₺");
                        Line("Kalan", $"{M(inv.Remaining)} ₺", bold: true);
                    }
                });

                if (!string.IsNullOrWhiteSpace(inv.Description))
                    col.Item().PaddingTop(14).Text($"Açıklama: {inv.Description}");
                if (inv.Type == "SA" && !string.IsNullOrWhiteSpace(firm.Iban))
                    col.Item().PaddingTop(6).Text($"IBAN: {firm.Iban}").SemiBold();
                if (inv.Type == "SA" && !string.IsNullOrWhiteSpace(firm.InvoiceNote))
                    col.Item().PaddingTop(6).Text(firm.InvoiceNote).FontColor(Colors.Grey.Darken1);
            });

            page.Footer().AlignCenter().Text(t =>
            {
                t.Span("Bu belge e-Fatura değildir. ").FontSize(7).FontColor(Colors.Grey.Medium);
                t.Span("Sayfa ").FontSize(7).FontColor(Colors.Grey.Medium);
                t.CurrentPageNumber().FontSize(7).FontColor(Colors.Grey.Medium);
                t.Span(" / ").FontSize(7).FontColor(Colors.Grey.Medium);
                t.TotalPages().FontSize(7).FontColor(Colors.Grey.Medium);
            });
        }));

        return (doc.GeneratePdf(), $"{inv.DocumentNo}.pdf");
    }
}

/// <summary>Excel dışa aktarımları. Abonelik süresi dolsa da çalışır (veriler müşterinindir).</summary>
public sealed class ExcelExport(CustomerService customers, InvoiceService invoices, MovementService movements, ReportService reports)
{
    private static byte[] Build(string sheet, string[] headers, IEnumerable<object?[]> rows, int[]? moneyColumns = null)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(sheet);

        for (var i = 0; i < headers.Length; i++)
            ws.Cell(1, i + 1).Value = headers[i];
        ws.Row(1).Style.Font.Bold = true;
        ws.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#E6F4F1");

        var r = 2;
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Length; c++)
            {
                var cell = ws.Cell(r, c + 1);
                switch (row[c])
                {
                    case null: break;
                    case decimal d: cell.Value = d; break;
                    case DateTime dt: cell.Value = dt; cell.Style.DateFormat.Format = "dd.mm.yyyy"; break;
                    case int n: cell.Value = n; break;
                    // Metin hücreleri "=" ile başlıyorsa formül olarak yorumlanmasın (CSV/Excel enjeksiyonu).
                    case string s: cell.Value = s.Length > 0 && "=+-@".Contains(s[0]) ? "'" + s : s; break;
                    default: cell.Value = row[c]!.ToString(); break;
                }
            }
            r++;
        }

        foreach (var c in moneyColumns ?? [])
            ws.Column(c).Style.NumberFormat.Format = "#,##0.00";
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents(1, Math.Min(r, 200));

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<byte[]> CustomersAsync(long firm)
    {
        var page = await customers.ListAsync(firm, null, null, null, new PageQuery(1, 100));
        var all = new List<CustomerDto>(page.Items);
        for (var p = 2; (p - 1) * 100 < page.Total; p++)
            all.AddRange((await customers.ListAsync(firm, null, null, null, new PageQuery(p, 100))).Items);

        return Build("Cariler", ["Kod", "Unvan", "Tür", "Vergi/T.C. no", "Vergi dairesi", "Telefon", "E-posta", "İl", "Bakiye"],
            all.Select(c => new object?[] { c.Code, c.Title, c.Type switch { "MU" => "Müşteri", "TE" => "Tedarikçi", _ => "Her ikisi" },
                c.TaxNumber, c.TaxOffice, c.Phone, c.Email, c.City, c.Balance }), [9]);
    }

    public async Task<byte[]> InvoicesAsync(long firm, string? type, DateTime? from, DateTime? to)
    {
        var all = new List<InvoiceListItem>();
        for (var p = 1; ; p++)
        {
            var res = await invoices.ListAsync(firm, type, null, null, null, from, to, new PageQuery(p, 100));
            all.AddRange(res.Page.Items);
            if (p * 100 >= res.Page.Total) break;
        }

        return Build("Faturalar", ["Tarih", "Belge no", "Tür", "Cari", "Vade", "Toplam", "Ödenen", "Kalan", "Durum"],
            all.Select(i => new object?[] { i.Date, i.DocumentNo, i.Type == "SA" ? "Satış" : "Alış", i.CustomerTitle, i.DueDate, i.Total, i.Paid, i.Remaining, i.StatusLabel }),
            [6, 7, 8]);
    }

    public async Task<byte[]> MovementsAsync(long firm, string? type, DateTime? from, DateTime? to)
    {
        var all = new List<MovementDto>();
        for (var p = 1; ; p++)
        {
            var res = await movements.ListAsync(firm, type, null, null, from, to, null, new PageQuery(p, 100));
            all.AddRange(res.Items);
            if (p * 100 >= res.Total) break;
        }

        return Build("Kasa-Banka", ["Tarih", "Tür", "Hesap", "Karşı hesap", "Cari", "Kategori", "Belge no", "Açıklama", "Tutar", "KDV", "Durum"],
            all.Select(m => new object?[] { m.Date, m.TypeLabel, m.AccountName, m.TargetAccountName, m.CustomerTitle, m.CategoryName, m.DocumentNo,
                m.Description, m.Amount, m.VatAmount, m.Cancelled ? "İptal" : "" }), [9, 10]);
    }

    public async Task<byte[]> StatementAsync(long firm, long customerId, DateTime? from, DateTime? to)
    {
        var s = await customers.StatementAsync(firm, customerId, from, to);
        var rows = new List<object?[]> { new object?[] { s.From, "Devir", null, null, null, null, s.OpeningBalance } };
        rows.AddRange(s.Rows.Select(r => new object?[] { r.Date, r.KindLabel, r.DocumentNo, r.Description, r.Debit, r.Credit, r.Balance }));
        return Build("Ekstre", ["Tarih", "İşlem", "Belge no", "Açıklama", "Borç", "Alacak", "Bakiye"], rows, [5, 6, 7]);
    }

    public async Task<byte[]> AgingAsync(long firm)
    {
        var rows = await reports.AgingAsync(firm);
        return Build("Alacak Yaşlandırma", ["Cari", "Vadesi gelmemiş", "0-30 gün", "31-60 gün", "61-90 gün", "90+ gün", "Toplam açık", "Cari bakiye"],
            rows.Select(a => new object?[] { a.CustomerTitle, a.NotDue, a.D0To30, a.D31To60, a.D61To90, a.Over90, a.Total, a.Balance }), [2, 3, 4, 5, 6, 7, 8]);
    }
}

[Route("api/export"), Authorize(Policy = Policies.FirmRead)]
public sealed class ExportController(ExcelExport excel) : ApiControllerBase
{
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private static string Stamp => DateTime.Now.ToString("yyyyMMdd");

    [HttpGet("customers")]
    public async Task<IActionResult> Customers() => File(await excel.CustomersAsync(Me.Firm), Xlsx, $"cariler-{Stamp}.xlsx");

    [HttpGet("invoices")]
    public async Task<IActionResult> Invoices(string? type, DateTime? from, DateTime? to) => File(await excel.InvoicesAsync(Me.Firm, type, from, to), Xlsx, $"faturalar-{Stamp}.xlsx");

    [HttpGet("movements")]
    public async Task<IActionResult> Movements(string? type, DateTime? from, DateTime? to) => File(await excel.MovementsAsync(Me.Firm, type, from, to), Xlsx, $"kasa-banka-{Stamp}.xlsx");

    [HttpGet("statement/{id:long}")]
    public async Task<IActionResult> Statement(long id, DateTime? from, DateTime? to) => File(await excel.StatementAsync(Me.Firm, id, from, to), Xlsx, $"ekstre-{id}-{Stamp}.xlsx");

    [HttpGet("aging")]
    public async Task<IActionResult> Aging() => File(await excel.AgingAsync(Me.Firm), Xlsx, $"alacak-yaslandirma-{Stamp}.xlsx");
}
