using System.Net;

namespace SmartSentinelEye.Shared.Kernel.Tests;

/// <summary>
/// The <c>Success(...)</c> / <c>Failure(...)</c> entry points a handler uses
/// instead of naming both type arguments (ADR-0047). What is worth pinning is
/// not that the value survives — that is the struct's job, covered by
/// <see cref="ResultTests"/> — but that the half-built outcome lands in the
/// right Result, and that the error arrives as its declared base rather than
/// the variant.
/// </summary>
public class ResultOutcomeTests
{
    private abstract record SampleError(string Code, string Message, HttpStatusCode Status)
        : ApiError(Code, Message, Status)
    {
        public sealed record NotFound(string Name)
            : SampleError("SAMPLE_NOT_FOUND", $"No '{Name}'.", HttpStatusCode.NotFound);
    }

#pragma warning disable CA1859 // Returning the base is the point: inference on the variant would build an outcome the Result cannot accept.
    private static class SampleFailures
    {
        public static SampleError NotFound(string name) => new SampleError.NotFound(name);
    }
#pragma warning restore CA1859

    [Fact]
    public void Success_converts_to_a_result_carrying_the_value()
    {
        Result<int, SampleError> result = Success(42);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public void Failure_converts_to_a_result_carrying_the_error()
    {
        Result<int, SampleError> result = Failure(SampleFailures.NotFound("thing"));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SAMPLE_NOT_FOUND");
    }

    [Fact]
    public void The_variant_survives_the_trip_through_the_base()
    {
        // The factory returns the base so inference lands there, but the
        // instance is still the variant — pattern matching on it has to keep
        // working or every ToProblem/switch on the error breaks.
        Result<int, SampleError> result = Failure(SampleFailures.NotFound("thing"));

        result.Error.ShouldBeOfType<SampleError.NotFound>()
            .Name.ShouldBe("thing");
    }

    [Fact]
    public void A_value_typed_more_narrowly_than_the_result_needs_naming()
    {
        // Generics are invariant, so an outcome built from int[] is not an
        // outcome of IReadOnlyList<int>. Naming the type argument is the way
        // through — the alternative is a cast at the call site.
        Result<IReadOnlyList<int>, SampleError> result = Success<IReadOnlyList<int>>([1, 2, 3]);

        result.Value.Count.ShouldBe(3);
    }

    [Fact]
    public void Both_entry_points_agree_with_the_structs_own_factories()
    {
        Result<int, SampleError> viaEntryPoint = Success(1);
        Result<int, SampleError> viaStruct = Result<int, SampleError>.Success(1);

        viaEntryPoint.IsSuccess.ShouldBe(viaStruct.IsSuccess);
        viaEntryPoint.Value.ShouldBe(viaStruct.Value);
    }
}
