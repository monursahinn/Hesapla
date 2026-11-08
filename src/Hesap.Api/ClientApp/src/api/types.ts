/** Sunucudaki DTO'ların karşılıkları (JSON camelCase). */

export interface Paged<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export type CustomerType = "MU" | "TE" | "IK";

export interface Customer {
  id: number;
  type: CustomerType;
  code: string;
  title: string;
  taxNumber?: string | null;
  taxOffice?: string | null;
  phone?: string | null;
  email?: string | null;
  address?: string | null;
  city?: string | null;
  district?: string | null;
  paymentTermDays: number;
  openingBalance: number;
  notes?: string | null;
  active: boolean;
  balance: number;
}

export interface StatementRow {
  date: string;
  kind: string;
  kindLabel: string;
  documentNo?: string | null;
  description?: string | null;
  debit: number;
  credit: number;
  balance: number;
  refId?: number | null;
}

export interface Statement {
  customer: Customer;
  from: string;
  to: string;
  openingBalance: number;
  rows: StatementRow[];
  closingBalance: number;
}

export interface Product {
  id: number;
  type: "UR" | "HZ";
  code: string;
  name: string;
  unit: string;
  vatRate: number;
  salePrice: number;
  purchasePrice: number;
  trackStock: boolean;
  openingStock: number;
  criticalStock?: number | null;
  active: boolean;
  stock?: number | null;
  lowStock: boolean;
}

export interface Account {
  id: number;
  type: "KA" | "BA";
  name: string;
  bankName?: string | null;
  iban?: string | null;
  openingBalance: number;
  active: boolean;
  balance: number;
}

export interface Category {
  id: number;
  type: "GI" | "GE";
  name: string;
  active: boolean;
}

export type InvoiceType = "SA" | "AL";
export type InvoiceStatus = "BE" | "KO" | "OD" | "GE" | "IP";

export interface InvoiceListItem {
  id: number;
  type: InvoiceType;
  documentNo: string;
  customerId: number;
  customerTitle: string;
  date: string;
  dueDate: string;
  total: number;
  paid: number;
  remaining: number;
  cancelled: boolean;
  status: InvoiceStatus;
  statusLabel: string;
}

export interface InvoiceLine {
  order: number;
  productId?: number | null;
  description: string;
  quantity: number;
  unit: string;
  unitPrice: number;
  discountRate: number;
  vatRate: number;
  amount: number;
  vatAmount: number;
}

export interface Movement {
  id: number;
  type: "TA" | "OD" | "GI" | "GE" | "VI";
  typeLabel: string;
  date: string;
  accountId: number;
  accountName: string;
  targetAccountId?: number | null;
  targetAccountName?: string | null;
  customerId?: number | null;
  customerTitle?: string | null;
  invoiceId?: number | null;
  invoiceNo?: string | null;
  categoryId?: number | null;
  categoryName?: string | null;
  amount: number;
  vatRate: number;
  vatAmount: number;
  documentNo?: string | null;
  description?: string | null;
  cancelled: boolean;
  cancelReason?: string | null;
}

export interface Invoice {
  id: number;
  type: InvoiceType;
  documentNo: string;
  customerId: number;
  customerTitle: string;
  customerTaxNumber?: string | null;
  customerTaxOffice?: string | null;
  customerAddress?: string | null;
  date: string;
  dueDate: string;
  subtotal: number;
  discountTotal: number;
  vatTotal: number;
  total: number;
  paid: number;
  remaining: number;
  description?: string | null;
  cancelled: boolean;
  cancelReason?: string | null;
  lines: InvoiceLine[];
  payments: Movement[];
  vatBreakdown: { rate: number; baseAmount: number; vat: number }[];
}

export interface InvoiceListResult {
  page: Paged<InvoiceListItem>;
  sumTotal: number;
  sumRemaining: number;
}

export const CUSTOMER_TYPES = [
  { value: "MU", label: "Müşteri" },
  { value: "TE", label: "Tedarikçi" },
  { value: "IK", label: "Müşteri ve tedarikçi" },
];

export const VAT_RATES = [
  { value: "20", label: "%20" },
  { value: "10", label: "%10" },
  { value: "1", label: "%1" },
  { value: "0", label: "%0" },
];

export const UNITS = ["Adet", "Kg", "Gr", "Lt", "Metre", "m²", "m³", "Paket", "Koli", "Saat", "Gün", "Ay", "Hizmet"];
