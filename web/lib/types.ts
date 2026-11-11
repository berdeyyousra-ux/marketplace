// Shapes returned by the Marketplace API (see the OpenAPI document at /openapi/v1.json).
export type Money = { amount: string; currency: string };
export type Page<T> = { items: T[]; nextCursor: string | null };
export type ItemList<T> = { items: T[] };

export type Problem = { title?: string; detail?: string; code?: string; status?: number; errors?: Record<string, string[]> };

export type Me = {
  id: string;
  email: string;
  displayName: string;
  roles: string[];
  sellerProfile: { id: string; displayName: string; country: string } | null;
};

export type Category = { id: number; slug: string; name: string };
export type ProductSummary = {
  id: string;
  title: string;
  price: Money;
  categoryId: number;
  sellerId: string;
  publishedAt: string;
  coverImageUrl: string | null;
  sellerName: string | null;
};
export type Product = {
  id: string;
  sellerId: string;
  categoryId: number;
  title: string;
  description: string;
  price: Money;
  status: string;
  publishedAt: string | null;
  file: { name: string; sizeBytes: number; contentType: string } | null;
  coverImageUrl: string | null;
  sellerName?: string | null;
  fileType?: string | null;
  fileSizeBytes?: number | null;
  viewerOwns?: boolean;
};

export type OrderItem = { id: string; productId: string; title: string; unitPrice: Money; status: string; coverImageUrl: string | null };
export type OrderSummary = { id: string; status: string; total: Money; itemCount: number; createdAt: string; paidAt: string | null };
export type OrderDetail = {
  id: string;
  status: string;
  total: Money;
  createdAt: string;
  expiresAt: string;
  paidAt: string | null;
  items: OrderItem[];
  payment: { id: string; provider: string; status: string; amount: Money; createdAt: string } | null;
};
// publishableKey is the provider's public key for its payment form (T-064); null for a provider without one.
export type PaymentStart = { paymentId: string; provider: string; clientSecret: string; amount: Money; publishableKey: string | null };

export type LibraryItem = {
  orderItemId: string;
  productId: string;
  title: string;
  fileName: string | null;
  purchasedAt: string;
  expiresAt: string;
  downloadCount: number;
  status: string;
  coverImageUrl: string | null;
};
export type MyRefundRequest = {
  id: string;
  orderItemId: string;
  productTitle: string;
  status: string;
  decision: string | null;
  decisionNote: string | null;
  createdAt: string;
};

export type SellerProductSummary = {
  id: string;
  title: string;
  status: string;
  price: Money;
  createdAt: string;
  updatedAt: string;
  publishedAt: string | null;
  salesCount: number;
  coverImageUrl: string | null;
};
export type SellerSummary = { balance: Money; revenueLast30Days: Money; ordersLast30Days: number; refundsLast30Days: number; activeProducts: number };
export type MonthlyRevenue = { month: string; netSales: Money; platformFees: Money; sellerNet: Money; runningSellerNet: Money };
export type TopProduct = { productId: string; title: string; units: number; gross: Money };
export type RecentOrderLine = {
  orderId: string;
  orderItemId: string;
  productTitle: string;
  unitPrice: Money;
  sellerAmount: Money;
  status: string;
  paidAt: string;
};

export type AiSummary = { status: string; category: string | null; recommendedAction: string | null; summary: string | null; flags: string[] };
export type AdminRefundSummary = {
  id: string;
  productTitle: string;
  status: string;
  policyVerdict: string;
  downloadCount: number;
  createdAt: string;
  ai: AiSummary | null;
  buyerName: string | null;
  amount: Money | null;
};
export type AdminRefundDetail = {
  id: string;
  orderItemId: string;
  orderId: string;
  productTitle: string;
  unitPrice: Money;
  buyerMessage: string;
  status: string;
  policyVerdict: string;
  downloadCountAtRequest: number;
  createdAt: string;
  decision: string | null;
  decisionNote: string | null;
  overrideReason: string | null;
  decidedAt: string | null;
  executedAt: string | null;
  failureReason: string | null;
  aiSuggestion: {
    status: string;
    model: string;
    promptVersion: string;
    facts: Record<string, unknown>;
    suggestion: { category: string; summary: string; recommendedAction: string; confidence: number; reasons: string[]; flags: string[] } | null;
    error: string | null;
    createdAt: string;
  } | null;
  history: { action: string; actorType: string; occurredAt: string }[];
  buyer: {
    id: string;
    displayName: string | null;
    email: string | null;
    paidOrders: number;
    otherRequests: { id: string; productTitle: string; status: string; createdAt: string }[];
  } | null;
};
