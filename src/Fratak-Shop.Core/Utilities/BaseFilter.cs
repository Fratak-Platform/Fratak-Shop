namespace Fratak_Shop.Core.Utilities;

public class BaseFilter<TData, TParam> : BasePaginate where TData : BaseDto where TParam : BaseFilterParam
{
    public required List<TData> Data { get; set; } 
    public required TParam FilterParams { get; set; }
}

public class BaseFilterParam
{
    private int _pageId = 1;
    public int PageId
    {
        get => _pageId;
        set => _pageId = value < 1 ? 1 : value;
    }

    private int _take = 20;
    public int Take
    {
        get => _take;
        set
        {
            if (value < 1)
                _take = 20;
            else if (value > 100)
                _take = 100;
            else
                _take = value;
        }
    }
}
