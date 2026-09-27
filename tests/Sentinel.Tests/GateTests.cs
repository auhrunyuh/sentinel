using Sentinel.Core;

public class GateTests
{
    const string Diff = """
        diff --git a/App.Tests/PagingTests.cs b/App.Tests/PagingTests.cs
        --- a/App.Tests/PagingTests.cs
        +++ b/App.Tests/PagingTests.cs
        @@ -10,3 +10,8 @@
             [Fact]
             public void Old() { }
        +
        +    [Fact]
        +    public void TotalPages_PartialPage() { }
        +    [Theory, InlineData(1)] public async Task Inline(int x) { }
        +    public void Helper() { }
         }
        diff --git a/App.Tests/ShippingTests.cs b/App.Tests/ShippingTests.cs
        --- a/App.Tests/ShippingTests.cs
        +++ b/App.Tests/ShippingTests.cs
        @@ -5,3 +5,3 @@
         [Fact]
        -    public void Label() => Assert.Equal("a", X());
        +    public void Label() => Assert.Equal("b", X());
        diff --git a/App/Paging.cs b/App/Paging.cs
        --- a/App/Paging.cs
        +++ b/App/Paging.cs
        @@ -7,1 +7,1 @@
        -    [Fact] void NotATest() { }
        +    x
        diff --git a/App.Tests/Old.Tests.cs b/App.Tests/Old.Tests.cs
        deleted file mode 100644
        --- a/App.Tests/Old.Tests.cs
        +++ /dev/null
        @@ -1,1 +0,0 @@
        -class Old {}
        """;

    [Fact]
    public void NewTestMethods_FindsAddedFactsAndTheoriesOnly() =>
        Assert.Equal(["TotalPages_PartialPage", "Inline"], Gate.NewTestMethods(Diff));

    [Fact]
    public void ModifiedExistingTests_FlagsChangedAndDeletedTestFiles() =>
        Assert.Equal(["App.Tests/ShippingTests.cs", "App.Tests/Old.Tests.cs"], Gate.ModifiedExistingTests(Diff));

    [Theory]
    [InlineData("Building...\nApp.cs(3,5): error CS0103: The name 'Foo' does not exist\nBuild FAILED.", TestRunOutcome.BuildError)]
    [InlineData("Passed!  - Failed: 0, Passed: 3, Skipped: 0", TestRunOutcome.Passed)]
    [InlineData("Failed!  - Failed: 1, Passed: 2, Skipped: 0", TestRunOutcome.Failed)]
    [InlineData("Failed: 2, Passed: 0, Skipped: 0", TestRunOutcome.Failed)]
    public void ClassifyTestRun_DistinguishesBuildErrorFromRealFailure(string output, TestRunOutcome expected) =>
        Assert.Equal(expected, Gate.ClassifyTestRun(output));
}
