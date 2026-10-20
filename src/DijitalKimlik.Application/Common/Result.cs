namespace DijitalKimlik.Application.Common;

/// <summary>Beklenen bir iş hatası: makinece okunacak kod ve kullanıcıya gösterilecek mesaj.</summary>
public sealed record Error(string Code, string Message);

/// <summary>
/// Result deseni: beklenen hatalar (kayıt zaten var, PIN yanlış) exception ile değil dönüş
/// değeriyle bildirilir. Exception yalnız beklenmeyen durumlar (veritabanı kapalı) içindir.
/// Böylece hata yolu metot imzasında görünür ve çağıran onu ele almak zorunda kalır.
/// </summary>
public class Result
{
    protected Result(Error? error) => Error = error;

    public Error? Error { get; }

    public bool IsSuccess => Error is null;

    public static Result Success() => new(null);

    public static Result Failure(Error error) => new(error);

    public static implicit operator Result(Error error) => Failure(error);
}

public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(T value)
        : base(null) => _value = value;

    private Result(Error error)
        : base(error)
    {
    }

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Başarısız sonucun değeri okunamaz: {Error!.Code}");

    public static Result<T> Success(T value) => new(value);

    public static new Result<T> Failure(Error error) => new(error);

    public static implicit operator Result<T>(T value) => Success(value);

    public static implicit operator Result<T>(Error error) => Failure(error);
}
