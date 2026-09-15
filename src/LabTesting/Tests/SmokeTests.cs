using System.Threading.Tasks;
using PlayerRoles;

namespace LabTesting.Tests;

/// <summary>
/// Checks that the clock works: yielding ticks and frames, and the three timing assertions.
/// </summary>
/// <remarks>
/// These tests need no third-party plugin. If they pass on a real server, the pump and the
/// synchronization context behave as intended. They also serve as the shortest example of the
/// syntax.
/// </remarks>
[Collection("Smoke")]
public sealed class PumpTests
{
    [Fact]
    public async Task Ceder_des_ticks_fait_avancer_le_compteur()
    {
        long before = Pump.Tick;
        await Expect.Tick(3);
        Assert.True(Pump.Tick >= before + 3, "3 ticks cédés doivent avancer le compteur d'au moins 3");
    }

    [Fact]
    public async Task Ceder_des_frames_fait_avancer_le_compteur()
    {
        long before = Pump.Frame;
        await Expect.Frame(2);
        Assert.True(Pump.Frame >= before + 2, "2 frames cédées doivent avancer le compteur d'au moins 2");
    }

    [Fact]
    public async Task Eventually_reussit_quand_la_condition_devient_vraie()
    {
        long target = Pump.Tick + 5;
        await Expect.Eventually(() => Pump.Tick >= target, 60.Ticks(), "le tick cible doit être atteint");
    }

    [Fact]
    public async Task Always_tient_sur_un_invariant_stable()
    {
        await Expect.Always(() => Pump.Installed, 10.Ticks(), "la pompe reste installée");
    }

    [Fact]
    public async Task Never_tient_quand_rien_ne_survient()
    {
        await Expect.Never(() => Pump.Tick < 0, 10.Ticks(), "le compteur de ticks ne devient jamais négatif");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public async Task Theory_recoit_bien_ses_arguments(int ticks)
    {
        long before = Pump.Tick;
        await Expect.Tick(ticks);
        Assert.True(Pump.Tick >= before + ticks, "cède " + ticks + " ticks");
    }
}

/// <summary>
/// Checks player creation: readiness, role assignment, and per-player network probes.
/// </summary>
[Collection("Smoke")]
public sealed class WorldTests
{
    [Fact, Isolation(Dirty.Dummies), Seed(1337)]
    public async Task Un_dummy_spawne_est_pret_et_a_son_role()
    {
        TestPlayer player = await World.Spawn(RoleTypeId.ClassD, "Smoke");

        Assert.True(player.Hub.IsDummy, "le hub doit être en ClientInstanceMode.Dummy");
        Assert.Equal(RoleTypeId.ClassD, player.Hub.roleManager.CurrentRole.RoleTypeId);
        await Swallowed.AssertNone();
    }

    /// <summary>
    /// Verifies that the spawn burst really does go through the overridden send of a recording
    /// dummy connection.
    /// </summary>
    /// <remarks>
    /// This is the experimental check behind the whole network observation feature. If it fails,
    /// <see cref="INetProbe"/> observes nothing and every network assertion is meaningless.
    /// </remarks>
    [Fact, Isolation(Dirty.Dummies)]
    public async Task Le_dummy_recoit_sa_rafale_de_spawn()
    {
        TestPlayer player = await World.Spawn(RoleTypeId.Spectator, "Probe");

        await player.Net.ExpectAnySend(120.Ticks());
        Assert.True(player.Net.SentCount > 0, "la connexion enregistreuse doit avoir capturé des octets");
    }

    [Fact, Isolation(Dirty.Dummies)]
    public async Task Deux_dummies_ont_des_connexions_distinctes()
    {
        var pair = await World.SpawnPair(RoleTypeId.ClassD, RoleTypeId.Scientist);

        Assert.True(!ReferenceEquals(pair.Item1.Net, pair.Item2.Net), "une sonde par dummy");
        Assert.True(pair.Item1.Hub != pair.Item2.Hub, "deux hubs distincts");
        await Expect.Tick();
    }
}

/// <summary>
/// Checks command driving and the event catalogue.
/// </summary>
[Collection("Smoke")]
public sealed class CommandTests
{
    /// <summary>
    /// Checks that an unknown command is reported as an unsuccessful result rather than throwing.
    /// </summary>
    [Fact]
    public async Task Une_commande_inconnue_est_signalee_et_non_levee()
    {
        CommandResult result = Commands.Run("commande_qui_nexiste_pas_" + Pump.Tick);

        Assert.False(result.Success, "une commande introuvable ne réussit pas");
        Assert.True(result.Response.Length > 0, "et elle explique pourquoi");
        await Expect.Tick();
    }

    [Fact]
    public async Task Le_catalogue_d_events_est_peuple()
    {
        // The exact count is tied to the LabAPI version, so only a plausible lower bound is
        // asserted. Version 1.1.7 declares 370 typed events.
        Assert.True(EventCatalog.Count > 300, "le catalogue doit contenir les events de LabAPI 1.1.x");
        await Expect.Tick();
    }
}
