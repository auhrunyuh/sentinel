using OrderService.Domain;

public class Bug01Tests
{
    [Fact]
    public void TotalPages_CountsPartialLastPage()
    {
        var page = Paging.Paginate(Enumerable.Range(1, 21).ToArray(), 3, 10);
        Assert.Equal(3, page.TotalPages);
        Assert.Equal([21], page.Items);
    }
}
