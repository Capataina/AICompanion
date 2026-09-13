extern alias live;

using Prepared = live::AICompanion.Companion.Brain.Infrastructure.Selection.PreparedActivity;
using Context = live::AICompanion.Companion.Brain.Infrastructure.Selection.ActivityComparisonContext;
using Evaluator = live::AICompanion.Companion.Brain.Infrastructure.Selection.EvaluatePreparedActivities;
using Families = live::AICompanion.Companion.Brain.Infrastructure.Selection.NominateFamilyActivities;
using Family = live::AICompanion.Companion.Brain.Infrastructure.Selection.PurposeFamily;
using Candidate = live::AICompanion.Companion.Brain.Infrastructure.Selection.FamilyCandidate;
using Offer = live::AICompanion.Companion.Brain.Activities.OfferEligibility;

/// <summary>
/// P03's named family-offer contract: on identical evaluated candidates the three nominations pick
/// the same winner as a flat maximum, an empty or invalid family cannot win, and a deferred child
/// is not an absence that another family can hide behind.
/// </summary>
internal static class VerifyFamilyOffers
{
    public static int Run()
    {
        var context = new Context(.2f, false, 10, 20, 100, 1.25f, true, .5f);
        Prepared[] board =
        {
            new(0, "mine", .6f, 90, true, true, false, true, Offer.Usable),
            new(1, "hunt", .5f, 40, true, true, false, false, Offer.Usable),
            new(2, "company", .4f, 0, false, false, true, false, Offer.Usable),
        };
        var evaluated = Evaluator.Evaluate(board, context);
        var grouped = new[]
        {
            new Candidate(Family.Gathering, evaluated[0]),
            new Candidate(Family.Combat, evaluated[1]),
            new Candidate(Family.NearbyAssistance, evaluated[2]),
        };
        var flatWinner = evaluated.Where(c => c.Error.Length == 0 && c.Final > 0)
            .OrderByDescending(c => c.Final).ThenBy(c => c.Index).Select(c => (int?)c.Index).FirstOrDefault();
        Require(Families.Select(Families.Nominate(grouped))?.Index == flatWinner,
            "family nominations must match the independent flat reference on identical evaluated candidates");
        Require(Families.Select(Families.Nominate(grouped.Reverse().Concat(grouped).ToArray()))?.Index == flatWinner,
            "reordering or duplicating prepared candidates must not change the winner");
        Require(Families.Nominate(Array.Empty<Candidate>()).Length == 3
            && Families.Select(Families.Nominate(Array.Empty<Candidate>())) == null,
            "three empty families must expose no nomination and select no activity");
        var emptyCombat = grouped.Where(c => c.Family != Family.Combat).ToArray();
        Require(Families.Nominate(emptyCombat).Single(n => n.Family == Family.Combat).Activity == null,
            "a family with no children nominates nothing");
        Require(Families.Select(Families.Nominate(emptyCombat))?.Index == flatWinner,
            "an empty family must not change which remaining child wins");
        var deferredBoard = Evaluator.Evaluate(new[]
        {
            board[0] with { Eligibility = Offer.Deferred, RawValue = .9f },
            board[1],
            board[2],
        }, context);
        Require(deferredBoard[0].Final == 0,
            "a deferred child must evaluate to no value");
        Require(Families.Select(Families.Nominate(new[]
        {
            new Candidate(Family.Gathering, deferredBoard[0]),
            new Candidate(Family.Combat, deferredBoard[1]),
            new Candidate(Family.NearbyAssistance, deferredBoard[2]),
        }))?.Index != 0,
            "a deferred child must not win, and must not hide a usable sibling in another family");
        foreach (var absent in new[] { Offer.NoOpportunity, Offer.PolicyForbidden, Offer.KnownUnusable, Offer.Deferred })
        {
            var rejected = Evaluator.Evaluate(new[] { board[0] with { Eligibility = absent, RawValue = .9f } }, context).Single();
            Require(rejected.Final == 0,
                $"a {absent} offer must not carry positive value into the parent comparison");
        }
        var zero = evaluated[0] with { Final = 0 };
        var invalid = evaluated[1] with { Final = float.PositiveInfinity, Error = "invalid" };
        Require(Families.Select(Families.Nominate(new[] { new Candidate(Family.Combat, zero), new Candidate(Family.Gathering, invalid) })) == null,
            "zero or invalid children must not manufacture a valuable family");
        var tie = evaluated[1] with { Final = evaluated[0].Final };
        Require(Families.Select(Families.Nominate(new[] { new Candidate(Family.Combat, tie), new Candidate(Family.NearbyAssistance, evaluated[0]) }))?.Index == evaluated[0].Index,
            "equal-valued children must use the same stable candidate order across families");
        Console.WriteLine("family offers: matched flat reference, empty family, deferred child and absent offers pass");
        return 0;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
