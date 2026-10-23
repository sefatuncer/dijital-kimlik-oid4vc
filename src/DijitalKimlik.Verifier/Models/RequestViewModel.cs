using DijitalKimlik.Verifier.Presentations;

namespace DijitalKimlik.Verifier.Models;

public sealed record RequestViewModel(Transaction Transaction, string WalletLink, string DeepLink, string QrDataUri);
