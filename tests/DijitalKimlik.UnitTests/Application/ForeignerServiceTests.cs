using DijitalKimlik.Application.Abstractions;
using DijitalKimlik.Application.Foreigners;
using DijitalKimlik.Domain.Foreigners;

using Microsoft.Extensions.Time.Testing;

namespace DijitalKimlik.UnitTests.Application;

public class ForeignerServiceTests
{
    private readonly FakeForeignerRepository _repository = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly ForeignerService _service;

    public ForeignerServiceTests() =>
        _service = new ForeignerService(_repository, _unitOfWork, new FakeTimeProvider(TestData.Now));

    private static RegisterForeignerCommand ValidCommand(string identityNumber = "99123456740") =>
        new(identityNumber, "Amina", "Haddad", new DateOnly(1995, 4, 12), "syr", "N1234567");

    [Fact]
    public async Task RegisterAsync_saves_new_foreigner()
    {
        // Arrange - Act - Assert (AAA) düzeni
        var result = await _service.RegisterAsync(ValidCommand());

        Assert.True(result.IsSuccess);
        Assert.Single(_repository.Items);
        Assert.Equal(1, _unitOfWork.SaveCount);
        Assert.Equal(TestData.Now, _repository.Items[0].CreatedAt);
    }

    [Fact]
    public async Task RegisterAsync_rejects_duplicate_without_saving()
    {
        await _service.RegisterAsync(ValidCommand());

        var result = await _service.RegisterAsync(ValidCommand());

        Assert.False(result.IsSuccess);
        Assert.Equal(ForeignerErrors.Duplicate, result.Error);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task RegisterAsync_maps_domain_rule_violation_to_error()
    {
        var command = ValidCommand() with { Nationality = "Suriye" };

        var result = await _service.RegisterAsync(command);

        Assert.Equal("foreigner.invalid_nationality", result.Error?.Code);
        Assert.Empty(_repository.Items);
    }

    [Fact]
    public async Task RegisterAsync_rejects_invalid_identity_number()
    {
        var result = await _service.RegisterAsync(ValidCommand("99123456741"));

        Assert.Equal(ForeignerErrors.InvalidIdentityNumber, result.Error);
    }

    // Mock kütüphanesi yerine elle yazılmış sahte nesneler (fake): davranışları açık ve okunaklı.
    private sealed class FakeForeignerRepository : IForeignerRepository
    {
        public List<Foreigner> Items { get; } = [];

        public Task<bool> ExistsAsync(ForeignerIdentityNumber identityNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.Any(f => f.IdentityNumber == identityNumber));

        public Task<Foreigner?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(f => f.Id == id));

        public void Add(Foreigner foreigner) => Items.Add(foreigner);
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.FromResult(1);
        }
    }
}
