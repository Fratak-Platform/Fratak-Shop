using Fratak_Shop.Web.Services;

namespace Fratak_Shop.Web.Infrastructure.Admin.Manage;

public enum MKind
{
    Text,
    TextArea,
    Html,
    Url,
    Slug,
    Color,
    Int,
    Decimal,
    Bool,
    Enum,
    Ref,
    Image,
    Date,
    Time,
    ReadOnly,
}

/// <summary>One field of a generic admin table (property of the entity).</summary>
public sealed record MField(string Name, string Label, MKind Kind = MKind.Text)
{
    public bool Required { get; init; }
    public bool InList { get; init; }
    public string? Help { get; init; }
    public bool Ltr { get; init; }
    public string? Placeholder { get; init; }

    /// <summary>Enum: Persian labels by member name (members without a label – except None – use the name).</summary>
    public IReadOnlyDictionary<string, string>? Labels { get; init; }

    /// <summary>Ref: the referenced entity and the property shown as its label.</summary>
    public Type? RefType { get; init; }
    public string RefLabel { get; init; } = "Name";

    /// <summary>Image: upload area; when <see cref="StoreAsPath"/> the full /uploads/… path is stored.</summary>
    public UploadArea? Area { get; init; }
    public bool StoreAsPath { get; init; }

    /// <summary>Slug: source field used when the slug is left empty.</summary>
    public string? SlugFrom { get; init; }

    /// <summary>Takes the full row of the form grid.</summary>
    public bool Wide { get; init; }
    public int Rows { get; init; } = 3;
}

/// <summary>Definition of a table managed by the generic admin engine (<c>/admin/manage/{key}</c>).</summary>
public sealed class MDef
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public required string Singular { get; init; }
    public required string Group { get; init; }
    public required Type Entity { get; init; }
    public required string[] Permissions { get; init; }
    public required MField[] Fields { get; init; }
    public string? Description { get; init; }
    public string Icon { get; init; } = "Sliders";
    public string[] Search { get; init; } = [];
    public string Sort { get; init; } = "DisplayOrder";
    public bool SortDescending { get; init; }

    /// <summary>Foreign key that scopes the list (e.g. videos of a product: <c>?parent=</c>).</summary>
    public string? ParentField { get; init; }
    public bool HasSeo { get; init; }
    public bool CanCreate { get; init; } = true;
    public bool CanDelete { get; init; } = true;

    /// <summary>Public page of a row, e.g. <c>/page/{Slug}</c>.</summary>
    public string? PublicUrl { get; init; }

    /// <summary>Related tables shown as links on the edit page: (title, key, parent field of the child).</summary>
    public (string Title, string Key)[] Children { get; init; } = [];

    public MField? Field(string name) => Fields.FirstOrDefault(f => f.Name == name);
}

public sealed record ManageList(IReadOnlyList<object> Rows, int Total, int Page, int Take, IReadOnlyDictionary<string, Dictionary<Guid, string>> RefLabels)
{
    public int PageCount => Total == 0 ? 1 : (int)Math.Ceiling(Total / (double)Take);
}
