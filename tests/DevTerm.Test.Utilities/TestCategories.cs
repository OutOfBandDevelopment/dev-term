namespace DevTerm.Test.Utilities;

/// <summary>
/// Recognized <c>[TestCategory]</c> values, enforced by
/// <c>DevTerm.CodingStandards.Tests.TestCategoryStandardsTests</c>. <see cref="Unit"/>/
/// <see cref="Integration"/> pick the CI-safe subset (see docs/coding-standards.md); everything
/// else here is a secondary category — transport, device profile type, or device make/model — so
/// <c>dotnet test --filter TestCategory=&lt;X&gt;</c> also works as targeted regression testing
/// across both tiers (e.g. every Serial-transport test, Unit and Integration alike), not just
/// CI/CD batching. Applied wherever a test class/method genuinely exercises that transport/profile/
/// device — a synthetic/mocked test still gets the transport or profile-type category if it
/// exercises that transport's or profile type's real logic, but only a real-hardware test gets a
/// specific device category tied to one physical unit.
/// </summary>
public static class TestCategories
{
    public const string Unit = nameof(Unit);
    public const string Integration = nameof(Integration);
    public const string Hardware = nameof(Hardware);

    // Transports.
    public const string Serial = nameof(Serial);
    public const string Tcp = nameof(Tcp);
    public const string Hid = nameof(Hid);
    public const string Usbtmc = nameof(Usbtmc);
    public const string Loopback = nameof(Loopback);

    // Device profile types.
    public const string Scpi = nameof(Scpi);

    // Device make/model — one real physical unit each.
    public const string Tektronix_2230 = nameof(Tektronix_2230);
    public const string Tektronix_Tds2024 = nameof(Tektronix_Tds2024);
    public const string Korad_Ka3005p = nameof(Korad_Ka3005p);
    public const string Korad_Ka6003p = nameof(Korad_Ka6003p);
    public const string Hp_34401a = nameof(Hp_34401a);
    public const string Rigol_Ds1102e = nameof(Rigol_Ds1102e);
    public const string Rigol_Dm3058e = nameof(Rigol_Dm3058e);
    public const string Rigol_Dg1022 = nameof(Rigol_Dg1022);
    public const string Rigol_Dg1062z = nameof(Rigol_Dg1062z);
    public const string Velleman_K8055 = nameof(Velleman_K8055);
    public const string Kuando_Busylight = nameof(Kuando_Busylight);
}
