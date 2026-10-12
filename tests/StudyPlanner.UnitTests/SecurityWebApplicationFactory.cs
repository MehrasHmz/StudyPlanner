using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StudyPlanner.Domain.Entities;
using StudyPlanner.Domain.Enums;
using StudyPlanner.Domain.Interfaces;

namespace StudyPlanner.UnitTests;

public sealed class SecurityWebApplicationFactory : WebApplicationFactory<Program>
{
    public SecurityTestStore Store { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Serilog:MinimumLevel:Default"] = "Fatal"
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ILoggerFactory>();
            services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.RemoveAll<IUserRepository>();
            services.RemoveAll<ICategoryRepository>();
            services.RemoveAll<IStudyItemRepository>();
            services.RemoveAll<IStudySessionRepository>();
            services.RemoveAll<IProgressRecordRepository>();
            services.AddSingleton(Store.UserRepository.Object);
            services.AddSingleton(Store.CategoryRepository.Object);
            services.AddSingleton(Store.ItemRepository.Object);
            services.AddSingleton(Store.SessionRepository.Object);
            services.AddSingleton(Store.ProgressRepository.Object);
        });
    }

    public HttpClient NewClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost")
    });
}

public sealed class SecurityTestStore
{
    public const string OwnerEmail = "owner@example.test";
    public const string OwnerPassword = "password123";
    public User Owner { get; } = new("Test Owner", OwnerEmail, "ijB+qBaQa257yaJeoHCrzR/fBHVJ4IHm+mpkiCk7GY0=");
    public Category Category { get; } = new("Test category", "Test description");
    public Dictionary<Guid, User> Users { get; } = new();
    public Dictionary<Guid, Category> Categories { get; } = new();
    public Dictionary<Guid, StudyItem> Items { get; } = new();
    public Dictionary<Guid, StudySession> Sessions { get; } = new();
    public List<ProgressRecord> Progress { get; } = new();
    public StudyItem OwnItem { get; }
    public StudyItem ForeignItem { get; }
    public StudySession OwnSession { get; }
    public StudySession ForeignSession { get; }
    public int Writes { get; private set; }
    public int CredentialLookups { get; private set; }
    public Mock<IUserRepository> UserRepository { get; } = new();
    public Mock<ICategoryRepository> CategoryRepository { get; } = new();
    public Mock<IStudyItemRepository> ItemRepository { get; } = new();
    public Mock<IStudySessionRepository> SessionRepository { get; } = new();
    public Mock<IProgressRecordRepository> ProgressRepository { get; } = new();

    public SecurityTestStore()
    {
        Users.Add(Owner.Id, Owner);
        Categories.Add(Category.Id, Category);
        OwnItem = Item(Owner.Id);
        ForeignItem = Item(Guid.NewGuid());
        Items.Add(OwnItem.Id, OwnItem);
        Items.Add(ForeignItem.Id, ForeignItem);
        OwnSession = Session(Owner.Id, OwnItem);
        ForeignSession = Session(ForeignItem.UserId, ForeignItem);
        Sessions.Add(OwnSession.Id, OwnSession);
        Sessions.Add(ForeignSession.Id, ForeignSession);

        UserRepository.Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => CredentialLookups++)
            .ReturnsAsync((string email, CancellationToken _) => Users.Values.FirstOrDefault(u => u.Email == email.ToLowerInvariant()));
        UserRepository.Setup(r => r.ExistsByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string email, CancellationToken _) => Users.Values.Any(u => u.Email == email.ToLowerInvariant()));
        UserRepository.Setup(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Callback<User, CancellationToken>((user, _) => { Users.Add(user.Id, user); Writes++; })
            .ReturnsAsync((User user, CancellationToken _) => user);
        UserRepository.Setup(r => r.Update(It.IsAny<User>())).Callback<User>(_ => Writes++);
        UserRepository.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Callback(() => Writes++).ReturnsAsync(1);

        CategoryRepository.Setup(r => r.GetPagedAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int page, int size, CancellationToken _) => (IReadOnlyList<Category>)Categories.Values
                .Where(c => !c.IsDeleted).Skip((page - 1) * size).Take(size).ToList());
        CategoryRepository.Setup(r => r.GetCountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => Categories.Values.Count(c => !c.IsDeleted));
        CategoryRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => Categories.GetValueOrDefault(id));
        CategoryRepository.Setup(r => r.AddAsync(It.IsAny<Category>(), It.IsAny<CancellationToken>()))
            .Callback<Category, CancellationToken>((category, _) => { Categories.Add(category.Id, category); Writes++; })
            .ReturnsAsync((Category category, CancellationToken _) => category);
        CategoryRepository.Setup(r => r.Update(It.IsAny<Category>())).Callback<Category>(_ => Writes++);
        CategoryRepository.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Callback(() => Writes++).ReturnsAsync(1);

        ItemRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => Items.GetValueOrDefault(id));
        ItemRepository.Setup(r => r.GetWithDetailsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => Items.GetValueOrDefault(id));
        ItemRepository.Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => (IReadOnlyList<StudyItem>)Items.Values.Where(i => i.UserId == id && !i.IsDeleted).ToList());
        ItemRepository.Setup(r => r.GetPagedAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid userId, int page, int size, string? search, string? sort, bool desc, CancellationToken _) =>
            {
                var items = Items.Values.Where(i => i.UserId == userId && !i.IsDeleted).ToList();
                return ((IReadOnlyList<StudyItem>)items.Skip((page - 1) * size).Take(size).ToList(), items.Count);
            });
        ItemRepository.Setup(r => r.AddAsync(It.IsAny<StudyItem>(), It.IsAny<CancellationToken>()))
            .Callback<StudyItem, CancellationToken>((item, _) => { Items.Add(item.Id, item); Writes++; })
            .ReturnsAsync((StudyItem item, CancellationToken _) => item);
        ItemRepository.Setup(r => r.Update(It.IsAny<StudyItem>())).Callback<StudyItem>(_ => Writes++);
        ItemRepository.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Callback(() => Writes++).ReturnsAsync(1);

        SessionRepository.Setup(r => r.GetWithItemsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => Sessions.GetValueOrDefault(id));
        SessionRepository.Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => (IReadOnlyList<StudySession>)Sessions.Values.Where(s => s.UserId == id).ToList());
        SessionRepository.Setup(r => r.AddAsync(It.IsAny<StudySession>(), It.IsAny<CancellationToken>()))
            .Callback<StudySession, CancellationToken>((session, _) => { Sessions.Add(session.Id, session); Writes++; })
            .ReturnsAsync((StudySession session, CancellationToken _) => session);
        SessionRepository.Setup(r => r.Update(It.IsAny<StudySession>())).Callback<StudySession>(_ => Writes++);
        SessionRepository.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Callback(() => Writes++).ReturnsAsync(1);
        ProgressRepository.Setup(r => r.AddAsync(It.IsAny<ProgressRecord>(), It.IsAny<CancellationToken>()))
            .Callback<ProgressRecord, CancellationToken>((progress, _) => { Progress.Add(progress); Writes++; })
            .ReturnsAsync((ProgressRecord progress, CancellationToken _) => progress);
        ProgressRepository.Setup(r => r.GetTotalMinutesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
    }

    private StudyItem Item(Guid userId) => new("Test item", "Description", StudyItemType.Programming,
        DifficultyLevel.Beginner, 20, 3, Category.Id, userId);

    private static StudySession Session(Guid userId, StudyItem item)
    {
        var session = new StudySession(userId, DateTime.UtcNow, 60);
        session.AddItem(new StudySessionItem(session.Id, item.Id, 20, 1));
        return session;
    }
}
