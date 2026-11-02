using System.Globalization;

namespace Marketplace.Tools.Seeding;

// Builds the dataset in memory. Popularity is skewed, so a few products and buyers account for many orders, and
// order volume grows over the two years of history, which gives the reports realistic shapes.
public sealed class SeedGenerator(SeedOptions options)
{
    private static readonly string[] _adjectives =
    [
        "Minimal", "Ultimate", "Cozy", "Modern", "Retro", "Bold", "Calm", "Bright", "Classic", "Clever",
        "Daily", "Elegant", "Fresh", "Handy", "Lucid", "Neat", "Quiet", "Rapid", "Simple", "Vivid",
    ];

    private static readonly string[] _studioNouns =
    [
        "Pixel", "Paper", "Atlas", "Harbor", "Maple", "Orbit", "Prism", "Quill", "Signal", "Willow",
    ];

    private static readonly string[][] _categoryNouns =
    [
        ["Guide to Budgeting", "Cookbook", "Field Guide", "Handbook", "Workbook", "Travel Journal"],
        ["Notion Budget Template", "Resume Template", "Weekly Planner", "Invoice Kit", "Habit Tracker", "Pitch Deck"],
        ["Icon Pack", "Illustration Set", "Texture Pack", "Mockup Bundle", "Pattern Library", "Logo Kit"],
        ["Lo-fi Beat Pack", "Sound Effects Kit", "Ambient Loops", "Podcast Intro", "Drum Samples", "Synth Presets"],
        ["Stock Footage Pack", "Transition Pack", "Motion Titles", "LUT Collection", "B-roll Bundle", "Intro Template"],
        ["Spreadsheet Toolkit", "Browser Extension", "Shell Scripts", "Plugin Bundle", "API Starter", "Automation Kit"],
        ["Display Font", "Serif Family", "Handwritten Font", "Mono Font", "Script Font", "Rounded Sans"],
        ["Printable Puzzle Set", "Wallpaper Set", "Craft Patterns", "Study Notes", "Coloring Pages", "Recipe Cards"],
    ];

    private static readonly string[] _extensions = ["pdf", "zip", "pdf", "zip", "zip", "zip", "zip", "pdf"];

    private static readonly string[] _contentTypes =
        ["application/pdf", "application/zip", "application/pdf", "application/zip", "application/zip", "application/zip", "application/zip", "application/pdf"];

    private static readonly string[] _countries = ["ES", "DE", "FR", "IT", "NL", "PT", "IE", "BE", "AT", "FI"];

    private readonly Random _random = new(options.Seed);

    public SeedDataset Generate()
    {
        var start = options.Now - SeedOptions.History;
        var admin = User("admin", 0, "Seed Admin", start - TimeSpan.FromDays(90));
        var sellerUsers = new SeedUser[options.Sellers];
        var sellers = new SeedSeller[options.Sellers];
        for (var i = 0; i < options.Sellers; i++)
        {
            var created = start - TimeSpan.FromDays(60) - TimeSpan.FromMinutes(_random.Next(0, 30 * 24 * 60));
            var name = $"{Pick(_adjectives)} {Pick(_studioNouns)} Studio {i + 1}";
            sellerUsers[i] = User("seller", i + 1, name, created);
            sellers[i] = new SeedSeller
            {
                Id = Ids.Version7(created + TimeSpan.FromMinutes(5), _random),
                UserIndex = i,
                DisplayName = name,
                Country = Pick(_countries),
                CreatedAt = created + TimeSpan.FromMinutes(5),
            };
        }

        var buyers = new SeedUser[options.Buyers];
        for (var i = 0; i < options.Buyers; i++)
        {
            var created = start - TimeSpan.FromDays(30) + TimeSpan.FromMinutes(_random.NextDouble() * SeedOptions.History.TotalMinutes * 0.5);
            buyers[i] = User("buyer", i + 1, $"Buyer {i + 1}", created);
        }

        var products = GenerateProducts(sellers, start);
        var (orders, items) = GenerateOrders(buyers, products, start);
        return new SeedDataset
        {
            Options = options,
            Admin = admin,
            SellerUsers = sellerUsers,
            Buyers = buyers,
            Sellers = sellers,
            Products = products,
            Orders = orders,
            Items = items,
        };
    }

    private SeedProduct[] GenerateProducts(SeedSeller[] sellers, DateTimeOffset start)
    {
        var products = new SeedProduct[options.Products];
        for (var i = 0; i < options.Products; i++)
        {
            var category = _random.Next(0, _categoryNouns.Length);
            var created = start - TimeSpan.FromDays(45) + TimeSpan.FromMinutes(_random.Next(0, 40 * 24 * 60));
            var id = Ids.Version7(created, _random);
            var title = $"{Pick(_adjectives)} {Pick(_categoryNouns[category])} {i + 1}";

            // Mostly between 2 and 60 euros, ending in .99.
            var price = Math.Max(1.99m, decimal.Round((decimal)Math.Exp(Math.Log(2) + (_random.NextDouble() * Math.Log(30))), 0) - 0.01m);
            var roll = _random.NextDouble();
            var status = roll < 0.94 ? "Published" : roll < 0.97 ? "Unpublished" : "Draft";
            var hasFile = status != "Draft";
            products[i] = new SeedProduct
            {
                Id = id,
                SellerIndex = _random.Next(0, sellers.Length),
                CategoryId = category + 1,
                Title = title,
                Description = $"{title}: a downloadable product for everyday use. Includes clear instructions and free updates.",
                Price = price,
                Status = status,
                FileKey = hasFile ? $"products/{id}/{Ids.Version7(created, _random)}" : null,
                FileName = hasFile ? $"{title.ToLowerInvariant().Replace(' ', '-')}.{_extensions[category]}" : null,
                FileSize = hasFile ? _random.Next(20_000, 50_000_000) : 0,
                FileContentType = hasFile ? _contentTypes[category] : null,
                FileSha256 = hasFile ? Hex(32) : null,
                CreatedAt = created,
                PublishedAt = hasFile ? created + TimeSpan.FromHours(_random.Next(1, 48)) : null,
            };
        }

        return products;
    }

    private (SeedOrder[] Orders, SeedItem[] Items) GenerateOrders(SeedUser[] buyers, SeedProduct[] allProducts, DateTimeOffset start)
    {
        // Only products that were ever published can be ordered. A random permutation spreads popularity across
        // sellers and categories.
        var orderable = Enumerable.Range(0, allProducts.Length).Where(index => allProducts[index].PublishedAt is not null).ToArray();
        _random.Shuffle(orderable);

        var orders = new SeedOrder[options.Orders];
        var items = new List<SeedItem>(options.Orders * 3 / 2);
        var owned = new HashSet<long>();
        var spanMinutes = SeedOptions.History.TotalMinutes;
        var picked = new List<int>(3);

        // Creation times grow over the history (density rises linearly), sorted so ids follow time.
        var times = new double[options.Orders];
        for (var i = 0; i < times.Length; i++)
        {
            times[i] = Math.Sqrt(_random.NextDouble()) * spanMinutes;
        }

        Array.Sort(times);

        for (var i = 0; i < orders.Length; i++)
        {
            var created = start + TimeSpan.FromMinutes(times[i]);
            var buyer = Skewed(buyers.Length, 1.6);
            var age = options.Now - created;
            var roll = _random.NextDouble();
            var status = age < TimeSpan.FromMinutes(30) ? SeedOrderStatus.Pending
                : roll < 0.86 ? SeedOrderStatus.Paid
                : roll < 0.94 ? SeedOrderStatus.Expired
                : SeedOrderStatus.Cancelled;

            var itemRoll = _random.NextDouble();
            var wanted = itemRoll < 0.6 ? 1 : itemRoll < 0.9 ? 2 : 3;
            picked.Clear();
            for (var attempt = 0; picked.Count < wanted && attempt < 30; attempt++)
            {
                var product = orderable[Skewed(orderable.Length, 3.0)];
                var key = ((long)buyer << 32) | (uint)product;
                if (picked.Contains(product) || allProducts[product].PublishedAt > created || owned.Contains(key))
                {
                    continue;
                }

                picked.Add(product);
            }

            if (picked.Count == 0)
            {
                // Rare: everything this buyer drew is owned. Fall back to the least popular end of the list.
                picked.Add(orderable[^(1 + (i % Math.Min(orderable.Length, 50)))]);
                if (owned.Contains(((long)buyer << 32) | (uint)picked[0]))
                {
                    status = SeedOrderStatus.Expired;
                }
            }

            var paidAt = status == SeedOrderStatus.Paid ? created + TimeSpan.FromSeconds(_random.Next(20, 600)) : (DateTimeOffset?)null;
            var order = new SeedOrder
            {
                Id = Ids.Version7(created, _random),
                BuyerIndex = buyer,
                Status = status,
                CreatedAt = created,
                PaidAt = paidAt,
                ClosedAt = status switch
                {
                    SeedOrderStatus.Expired => created + TimeSpan.FromMinutes(30) + TimeSpan.FromSeconds(_random.Next(1, 60)),
                    SeedOrderStatus.Cancelled => created + TimeSpan.FromSeconds(_random.Next(30, 1700)),
                    _ => null,
                },
                FirstItem = items.Count,
                ItemCount = picked.Count,
                HadFailedAttempt = status != SeedOrderStatus.Pending && _random.NextDouble() < (status == SeedOrderStatus.Paid ? 0.05 : 0.35),
            };
            order.PaymentId = Ids.Version7(created + TimeSpan.FromSeconds(10), _random);
            order.FailedPaymentId = Ids.Version7(created + TimeSpan.FromSeconds(5), _random);

            var refunded = 0;
            foreach (var product in picked)
            {
                var price = allProducts[product].Price;
                var fee = decimal.Round(price * SeedOptions.CommissionBasisPoints / 10000m, 2, MidpointRounding.AwayFromZero);
                var item = new SeedItem
                {
                    Id = Ids.Version7(created, _random),
                    OrderIndex = i,
                    ProductIndex = product,
                    UnitPrice = price,
                    PlatformFee = fee,
                };

                if (paidAt is not null)
                {
                    owned.Add(((long)buyer << 32) | (uint)product);
                    item.GrantId = Ids.Version7(paidAt.Value, _random);
                    item.DownloadCount = _random.Next(0, 4);
                    var refundAt = paidAt.Value + TimeSpan.FromHours(_random.Next(2, 13 * 24));
                    var refundRoll = _random.NextDouble();
                    if (refundRoll < 0.025 && refundAt < options.Now)
                    {
                        item.RefundedAt = refundAt;
                        item.RefundRequestId = Ids.Version7(refundAt - TimeSpan.FromHours(1), _random);
                        refunded++;
                    }
                    else if (refundRoll < 0.03 && refundAt < options.Now)
                    {
                        item.RefundRejected = true;
                        item.RefundRequestId = Ids.Version7(refundAt - TimeSpan.FromHours(1), _random);
                    }
                }

                order.Total += price;
                items.Add(item);
            }

            if (refunded > 0)
            {
                order.Status = refunded == picked.Count ? SeedOrderStatus.Refunded : SeedOrderStatus.PartiallyRefunded;
            }

            orders[i] = order;
        }

        return (orders, items.ToArray());
    }

    private SeedUser User(string kind, int number, string displayName, DateTimeOffset created) => new()
    {
        Id = Ids.Version7(created, _random),
        Email = number == 0 ? $"{kind}@{SeedOptions.EmailDomain}" : $"{kind}{number.ToString(CultureInfo.InvariantCulture)}@{SeedOptions.EmailDomain}",
        DisplayName = displayName,
        CreatedAt = created,
    };

    // Index in [0, count) with low indexes more likely; a higher power is more skewed.
    private int Skewed(int count, double power) => Math.Min(count - 1, (int)(Math.Pow(_random.NextDouble(), power) * count));

    private T Pick<T>(T[] values) => values[_random.Next(0, values.Length)];

    private string Hex(int bytes)
    {
        var buffer = new byte[bytes];
        _random.NextBytes(buffer);
        return Convert.ToHexStringLower(buffer);
    }
}

public static class Ids
{
    // A version 7 UUID for the given time, with the random part taken from the seeded generator.
    public static Guid Version7(DateTimeOffset time, Random random)
    {
        ArgumentNullException.ThrowIfNull(random);

        Span<byte> bytes = stackalloc byte[16];
        random.NextBytes(bytes);
        var milliseconds = time.ToUnixTimeMilliseconds();
        for (var i = 0; i < 6; i++)
        {
            bytes[i] = (byte)(milliseconds >> (8 * (5 - i)));
        }

        bytes[6] = (byte)(0x70 | (bytes[6] & 0x0F));
        bytes[8] = (byte)(0x80 | (bytes[8] & 0x3F));
        return new Guid(bytes, bigEndian: true);
    }

    // The time a version 7 UUID was made for, to the millisecond.
    public static DateTimeOffset CreatedAt(Guid id)
    {
        Span<byte> bytes = stackalloc byte[16];
        id.TryWriteBytes(bytes, bigEndian: true, out _);
        long milliseconds = 0;
        for (var i = 0; i < 6; i++)
        {
            milliseconds = (milliseconds << 8) | bytes[i];
        }

        return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
    }
}
