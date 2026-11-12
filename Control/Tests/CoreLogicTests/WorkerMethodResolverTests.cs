using System.Threading;
using System.Threading.Tasks;
using APS.SystemControl.Core.CoreClass;
using Xunit;

namespace APS.SystemControl.Tests.CoreLogic;

public class WorkerMethodResolverTests
{
    private class Fixture
    {
        public static Task<object?> Single(string a, float b, CancellationToken ct) => Task.FromResult<object?>(null);
        public static Task<object?> Dual(string a, float b, CancellationToken ct) => Task.FromResult<object?>(null);
        public static Task<object?> Dual(object dev, string a, float b, string c, CancellationToken ct) => Task.FromResult<object?>(null);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(99)]
    public void Resolve_single_method_returns_it_regardless_of_varcount(int varCount)
    {
        var m = WorkerMethodResolver.Resolve(typeof(Fixture), "Single", varCount);
        Assert.NotNull(m);
        Assert.Equal("Single", m!.Name);
    }

    [Fact]
    public void Resolve_overload_picks_by_arity_two_vars()
    {
        var m = WorkerMethodResolver.Resolve(typeof(Fixture), "Dual", varCount: 2);
        Assert.NotNull(m);
        Assert.Equal(3, m!.GetParameters().Length); // (string, float, ct)
    }

    [Fact]
    public void Resolve_overload_picks_by_arity_four_vars()
    {
        var m = WorkerMethodResolver.Resolve(typeof(Fixture), "Dual", varCount: 4);
        Assert.NotNull(m);
        Assert.Equal(5, m!.GetParameters().Length); // (object, string, float, string, ct)
    }

    [Fact]
    public void Resolve_unknown_name_returns_null()
    {
        Assert.Null(WorkerMethodResolver.Resolve(typeof(Fixture), "Nope", varCount: 1));
    }

    [Fact]
    public void Resolve_overloads_no_arity_match_returns_null()
    {
        // Fixture.Dual has effective arities 2 and 4; 3 matches neither.
        var m = WorkerMethodResolver.Resolve(typeof(Fixture), "Dual", varCount: 3);
        Assert.Null(m);
    }
}
