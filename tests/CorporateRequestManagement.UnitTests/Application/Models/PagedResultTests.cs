using CorporateRequestManagement.Application.Common.Exceptions;
using CorporateRequestManagement.Application.Common.Models;

namespace CorporateRequestManagement.UnitTests.Application.Models;

public sealed class PagedResultTests
{
    [Fact]
    public void Create_WhenFirstPageHasRemainder_ShouldCalculatePagination()
    {
        PagedResult<int> result = new([1, 2], 21, PageRequest.Create());

        Assert.Equal(2, result.TotalPages);
        Assert.False(result.HasPreviousPage);
        Assert.True(result.HasNextPage);
    }

    [Fact]
    public void Create_WhenTotalCountIsExactlyDivisible_ShouldCalculateTotalPages()
    {
        PagedResult<int> result = new([1], 40, PageRequest.Create());

        Assert.Equal(2, result.TotalPages);
    }

    [Fact]
    public void Create_WhenTotalCountIsZero_ShouldReturnZeroTotalPages()
    {
        PagedResult<int> result = new([], 0, PageRequest.Create());

        Assert.Equal(0, result.TotalPages);
        Assert.False(result.HasNextPage);
    }

    [Fact]
    public void Create_WhenPageIsInMiddle_ShouldExposePreviousAndNextPages()
    {
        PagedResult<int> result = new([21], 60, PageRequest.Create(2, 20));

        Assert.True(result.HasPreviousPage);
        Assert.True(result.HasNextPage);
    }

    [Fact]
    public void Create_WhenPageIsLast_ShouldNotExposeNextPage()
    {
        PagedResult<int> result = new([41], 41, PageRequest.Create(3, 20));

        Assert.True(result.HasPreviousPage);
        Assert.False(result.HasNextPage);
    }

    [Fact]
    public void Create_WhenRequestedPageExceedsTotalPages_ShouldAllowEmptyItems()
    {
        PagedResult<int> result = new([], 20, PageRequest.Create(3, 20));

        Assert.Empty(result.Items);
        Assert.Equal(1, result.TotalPages);
        Assert.True(result.HasPreviousPage);
        Assert.False(result.HasNextPage);
    }

    [Fact]
    public void Create_WhenTotalCountIsNegative_ShouldRejectResult()
    {
        ApplicationValidationException exception = Assert.Throws<ApplicationValidationException>(
            () => new PagedResult<int>([], -1, PageRequest.Create()));

        Assert.Equal("pagination.total_count_invalid", exception.Code);
    }

    [Fact]
    public void Create_WhenItemsAreNull_ShouldRejectResult()
    {
        Assert.Throws<ArgumentNullException>(
            () => new PagedResult<int>(null!, 0, PageRequest.Create()));
    }

    [Fact]
    public void Create_WhenPageRequestIsNull_ShouldRejectResult()
    {
        Assert.Throws<ArgumentNullException>(
            () => new PagedResult<int>([], 0, null!));
    }

    [Fact]
    public void Create_WhenSourceListChanges_ShouldPreserveMaterializedItems()
    {
        List<int> source = [1, 2];
        PagedResult<int> result = new(source, 2, PageRequest.Create());

        source.Add(3);

        Assert.Equal([1, 2], result.Items);
    }

    [Fact]
    public void Create_WhenItemsAreExposed_ShouldPreventCollectionMutation()
    {
        PagedResult<int> result = new([1, 2], 2, PageRequest.Create());
        ICollection<int> collection = Assert.IsAssignableFrom<ICollection<int>>(result.Items);

        Assert.True(collection.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => collection.Add(3));
        Assert.Equal([1, 2], result.Items);
    }

    [Fact]
    public void Create_WhenItemCountDiffersFromTotalCount_ShouldPreserveBothValues()
    {
        PagedResult<int> result = new([21, 22], 42, PageRequest.Create(2, 20));

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(42, result.TotalCount);
    }

    [Fact]
    public void Create_WhenTotalCountIsMaximumAndPageSizeIsOneHundred_ShouldCalculateWithoutOverflow()
    {
        PagedResult<int> result = new([], int.MaxValue, PageRequest.Create(1, 100));

        Assert.Equal(21_474_837, result.TotalPages);
    }

    [Fact]
    public void Create_WhenTotalCountIsMaximumAndPageSizeIsOne_ShouldCalculateWithoutOverflow()
    {
        PagedResult<int> result = new([], int.MaxValue, PageRequest.Create(1, 1));

        Assert.Equal(int.MaxValue, result.TotalPages);
    }

    [Fact]
    public void Create_WhenItemsAllowSingleEnumeration_ShouldEnumerateExactlyOnce()
    {
        SingleEnumerationEnumerable items = new();

        PagedResult<int> result = new(items, 2, PageRequest.Create());

        Assert.Equal([1, 2], result.Items);
        Assert.Equal(1, items.EnumerationCount);
    }

    private sealed class SingleEnumerationEnumerable : IEnumerable<int>
    {
        public int EnumerationCount { get; private set; }

        public IEnumerator<int> GetEnumerator()
        {
            EnumerationCount++;
            if (EnumerationCount > 1)
            {
                throw new InvalidOperationException("Enumerable birden fazla kez tüketildi.");
            }

            yield return 1;
            yield return 2;
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
