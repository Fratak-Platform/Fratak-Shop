#!/usr/bin/env python3
# ---------------------------------------------------------------------------
# Fratak-Shop — schema generator
#
# The .NET SDK is not available in every environment this repository is edited
# in, so this script produces the EF Core artefacts of the «ShopWebLayer»
# migration directly from the entity classes (which stay the single source of
# truth) plus the small spec table below that declares keys, indexes, foreign
# keys and check constraints — exactly the information that lives in the
# IEntityTypeConfiguration classes.
#
# It writes:
#   1. src/Fratak-Shop.Data/Migrations/<stamp>_ShopWebLayer.cs
#   2. src/Fratak-Shop.Data/Migrations/<stamp>_ShopWebLayer.Designer.cs
#   3. an updated FratakShopDbContextModelSnapshot.cs
#
# Running `dotnet ef migrations add` afterwards stays possible: the snapshot is
# kept in sync, so EF only re-canonicalises the file ordering.
#
# Usage:  python3 tools/dev/schema_migration.py
# ---------------------------------------------------------------------------
from __future__ import annotations

import os
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
ENTITIES = ROOT / "src/Fratak-Shop.Data/Entities"
MIGRATIONS = ROOT / "src/Fratak-Shop.Data/Migrations"
SNAPSHOT = MIGRATIONS / "FratakShopDbContextModelSnapshot.cs"

STAMP = "20261003120000"
MIGRATION_NAME = "ShopWebLayer"

# ---------------------------------------------------------------------------
# type mapping
# ---------------------------------------------------------------------------
SCALARS = {
    "Guid": ("uniqueidentifier", "Guid"),
    "string": ("nvarchar", "string"),
    "bool": ("bit", "bool"),
    "int": ("int", "int"),
    "long": ("bigint", "long"),
    "decimal": ("decimal", "decimal"),
    "double": ("float", "double"),
    "float": ("real", "float"),
    "DateTimeOffset": ("datetimeoffset", "DateTimeOffset"),
    "DateTime": ("datetime2", "DateTime"),
    "DateOnly": ("date", "DateOnly"),
    "TimeSpan": ("time", "TimeSpan"),
}

ENUM_TYPES = {
    "UserRole", "SettingValueType", "MenuLocation", "SocialPlatform", "BannerPosition",
    "PaymentGatewayProvider", "PaymentTransactionStatus", "BankTransferStatus",
    "CouponDiscountType", "NotificationType", "NotificationChannel", "SeoEntityType",
    "SeoAuditStatus", "OrderStatus", "PaymentMethod", "PaymentStatus", "SitePage", "UserTokenPurpose",
    "AlertPosition", "AlertType", "CommentType", "CommentStatus", "LocationType",
    "FeatureValueType", "RedirectType", "MessageType", "MessageStatus",
}


class Prop:
    def __init__(self, name, cs_type, nullable, required, max_length, precision):
        self.name = name
        self.cs_type = cs_type
        self.nullable = nullable
        self.required = required
        self.max_length = max_length
        self.precision = precision

    # ---------------------------------------------------------------- helpers
    @property
    def is_enum(self):
        return self.cs_type in ENUM_TYPES

    @property
    def is_string(self):
        return self.cs_type == "string"

    @property
    def is_decimal(self):
        return self.cs_type == "decimal" and not self.is_enum

    @property
    def clr(self):
        """CLR type used by the fluent API (enums are stored as int)."""
        if self.is_enum:
            return "int"
        return self.cs_type

    @property
    def column_type(self):
        if self.is_enum:
            return "int"
        if self.is_string:
            return f"nvarchar({self.max_length})" if self.max_length else "nvarchar(max)"
        if self.is_decimal:
            return "decimal(18,0)" if self.precision == 0 else "decimal(18,2)"
        return SCALARS[self.cs_type][0]

    @property
    def nullable_cs(self):
        if self.is_enum:
            return "int?"
        return f"{self.cs_type}?"


# ---------------------------------------------------------------------------
# entity parsing
# ---------------------------------------------------------------------------
PROP_RE = re.compile(
    r"^\s*public\s+([A-Za-z_][\w\.<>\?]*)\s+([A-Za-z_]\w*)\s*\{\s*get;\s*set;\s*\}\s*(=\s*[^;]+;)?\s*$")


def parse_entity(path: Path, skip: set[str], class_name: str | None = None) -> list[Prop]:
    lines = path.read_text(encoding="utf-8").splitlines()
    start, end = 0, len(lines)
    if class_name:
        start = next(i for i, line in enumerate(lines)
                     if re.match(rf"^public\s+(abstract\s+)?class\s+{class_name}\b", line))
        end = next(i for i in range(start + 1, len(lines)) if lines[i] == "}")
        lines = lines[start:end]
    props: list[Prop] = []
    for index, line in enumerate(lines):
        match = PROP_RE.match(line)
        if not match:
            continue
        cs_type, name = match.group(1), match.group(2)
        if name in skip or cs_type.startswith("List<"):
            continue
        # gather attributes directly above the property
        attributes: list[str] = []
        cursor = index - 1
        while cursor >= 0:
            stripped = lines[cursor].strip()
            if stripped.startswith("["):
                attributes.append(stripped)
                cursor -= 1
                continue
            if stripped.startswith("///") or stripped == "":
                cursor -= 1
                continue
            break

        blob = " ".join(attributes)
        max_length = None
        length_match = re.search(r"MaxLength\((\d+)\)", blob)
        if length_match:
            max_length = int(length_match.group(1))
        required = "[Required]" in blob

        if cs_type in ENUM_TYPES or cs_type.endswith("?"):  # enums / nullable scalars
            nullable = cs_type.endswith("?")
            base = cs_type.rstrip("?")
        else:
            nullable = False
            base = cs_type
        if base not in SCALARS and base not in ENUM_TYPES:
            # navigation property (class reference) — not a column
            continue
        props.append(Prop(name, base, nullable, required, max_length, 0))
    return props


def entity_class_name(clr: str) -> str:
    return clr.split(".")[-1]


def parse_checks(path: Path) -> list[tuple[str, str]]:
    """Reads the HasCheckConstraint calls of a configuration file."""
    text = path.read_text(encoding="utf-8")
    return re.findall(r'HasCheckConstraint\(\s*"([^"]+)"\s*,\s*"([^"]+)"', text)


def apply_precision(props: list[Prop], config_path: Path):
    """Reads `.HasPrecision(18, 0)` from the configuration file."""
    text = config_path.read_text(encoding="utf-8")
    for name, precision in re.findall(r"Property\(\w+ => \w+\.(\w+)\)\s*\.HasPrecision\(\d+,\s*(\d+)\)", text):
        for prop in props:
            if prop.name == name:
                prop.precision = int(precision)


BASE_ENTITY_PROPS = [
    ("CreatedAt", "DateTimeOffset", False),
    ("UpdatedAt", "DateTimeOffset", False),
    ("IsDeleted", "bool", False),
    ("DeletedAt", "DateTimeOffset", True),
]


def with_base_entity(props: list[Prop]) -> list[Prop]:
    result = [Prop("Id", "Guid", False, True, None, 0)] + props
    for name, cs_type, nullable in BASE_ENTITY_PROPS:
        result.append(Prop(name, cs_type, nullable, True, None, 0))
    return result


# ---------------------------------------------------------------------------
# spec of the new tables (mirrors the IEntityTypeConfiguration classes)
# ---------------------------------------------------------------------------
class Table:
    def __init__(self, clr, table, entity_file, config_file, skip=(), indexes=(), fks=(),
                 navigations=(), extra_snapshot=""):
        self.clr = clr
        self.name = clr.split(".")[-1]
        self.table = table
        self.entity_file = ENTITIES / entity_file
        self.config_file = ROOT / "src/Fratak-Shop.Data/Configurations" / config_file
        self.skip = set(skip)
        self.indexes = list(indexes)          # (props tuple, unique)
        self.fks = list(fks)                  # (prop, principal_clr, principal_prop, nav, withmany, ondelete)
        self.navigations = list(navigations)
        self.extra_snapshot = extra_snapshot
        self.checks = parse_checks(self.config_file)
        self.props = with_base_entity(parse_entity(self.entity_file, self.skip, self.name))
        apply_precision(self.props, self.config_file)

    def prop(self, name) -> Prop:
        for p in self.props:
            if p.name == name:
                return p
        raise KeyError(f"{name} not found on {self.name}")


USERS = "Fratak_Shop.Data.Entities.Users.User"
ORDERS = "Fratak_Shop.Data.Entities.Orders.Order"
COUPONS = "Fratak_Shop.Data.Entities.Marketing.Coupon"

TABLES: list[Table] = [
    Table(
        "Fratak_Shop.Data.Entities.Users.UserRoleAssignment", "UserRoleAssignments",
        "Users/UserRoleAssignment.cs", "UserRoleAssignmentConfiguration.cs",
        skip={"User", "GrantedByUser"},
        indexes=[(("UserId", "Role"), True), (("Role",), False)],
        fks=[
            ("UserId", USERS, "Id", "User", "RoleAssignments", "Cascade"),
            ("GrantedByUserId", USERS, "Id", "GrantedByUser", None, "Restrict"),
        ],
        navigations=["User", "GrantedByUser"],
    ),
    Table(
        "Fratak_Shop.Data.Entities.Users.UserToken", "UserTokens",
        "Users/UserToken.cs", "UserTokenConfiguration.cs",
        skip={"User"},
        indexes=[(("UserId", "Purpose"), False), (("ExpiresAt",), False)],
        fks=[("UserId", USERS, "Id", "User", None, "Cascade")],
        navigations=["User"],
    ),
    Table(
        "Fratak_Shop.Data.Entities.Settings.SiteSetting", "SiteSettings",
        "Settings/SiteSetting.cs", "SiteSettingConfiguration.cs",
        indexes=[(("Key",), True), (("Group",), False)],
    ),
    Table(
        "Fratak_Shop.Data.Entities.Navigation.MenuItem", "MenuItems",
        "Navigation/MenuItem.cs", "MenuItemConfiguration.cs",
        skip={"Parent", "Children"},
        indexes=[(("Location",), False), (("ParentId",), False), (("Location", "DisplayOrder"), False)],
        fks=[("ParentId", "Fratak_Shop.Data.Entities.Navigation.MenuItem", "Id", "Parent", "Children", "Restrict")],
        navigations=["Parent", "Children"],
    ),
    Table(
        "Fratak_Shop.Data.Entities.Navigation.SocialLink", "SocialLinks",
        "Navigation/SocialLink.cs", "SocialLinkConfiguration.cs",
        indexes=[(("Platform",), False), (("IsActive",), False)],
    ),
    Table(
        "Fratak_Shop.Data.Entities.Payments.PaymentCard", "PaymentCards",
        "Payments/PaymentCard.cs", "PaymentCardConfiguration.cs",
        indexes=[(("CardNumber",), True), (("IsActive",), False)],
    ),
    Table(
        "Fratak_Shop.Data.Entities.Orders.ShippingMethod", "ShippingMethods",
        "Orders/ShippingMethod.cs", "ShippingMethodConfiguration.cs",
        indexes=[(("IsActive",), False)],
    ),
    Table(
        "Fratak_Shop.Data.Entities.Marketing.Coupon", "Coupons",
        "Marketing/Coupon.cs", "CouponConfiguration.cs",
        skip={"Usages"},
        indexes=[(("Code",), True), (("IsActive",), False)],
        navigations=["Usages"],
    ),
    Table(
        "Fratak_Shop.Data.Entities.Payments.PaymentTransaction", "PaymentTransactions",
        "Payments/PaymentTransaction.cs", "PaymentTransactionConfiguration.cs",
        skip={"Order"},
        indexes=[(("OrderId",), False), (("Status",), False), (("Authority",), False)],
        fks=[("OrderId", ORDERS, "Id", "Order", "PaymentTransactions", "Cascade")],
        navigations=["Order"],
    ),
    Table(
        "Fratak_Shop.Data.Entities.Payments.BankTransferPayment", "BankTransferPayments",
        "Payments/BankTransferPayment.cs", "BankTransferPaymentConfiguration.cs",
        skip={"Order", "PaymentCard", "ReviewedByUser", "Receipts"},
        indexes=[(("OrderId",), True), (("Status",), False)],
        fks=[
            ("OrderId", ORDERS, "Id", "Order", None, "Cascade", "one-to-one"),
            ("PaymentCardId", "Fratak_Shop.Data.Entities.Payments.PaymentCard", "Id", "PaymentCard", None, "SetNull"),
            ("ReviewedByUserId", USERS, "Id", "ReviewedByUser", None, "SetNull"),
        ],
        navigations=["Order", "PaymentCard", "ReviewedByUser", "Receipts"],
    ),
    Table(
        "Fratak_Shop.Data.Entities.Payments.BankTransferReceipt", "BankTransferReceipts",
        "Payments/BankTransferPayment.cs", "BankTransferReceiptConfiguration.cs",
        skip={"BankTransferPayment"},
        indexes=[(("BankTransferPaymentId",), False)],
        fks=[("BankTransferPaymentId", "Fratak_Shop.Data.Entities.Payments.BankTransferPayment", "Id",
              "BankTransferPayment", "Receipts", "Cascade")],
        navigations=["BankTransferPayment"],
        extra_snapshot="",
    ),
    Table(
        "Fratak_Shop.Data.Entities.Orders.OrderStatusHistory", "OrderStatusHistories",
        "Orders/OrderStatusHistory.cs", "OrderStatusHistoryConfiguration.cs",
        skip={"Order", "ChangedByUser"},
        indexes=[(("OrderId",), False), (("Status",), False)],
        fks=[
            ("OrderId", ORDERS, "Id", "Order", "StatusHistory", "Cascade"),
            ("ChangedByUserId", USERS, "Id", "ChangedByUser", None, "SetNull"),
        ],
        navigations=["Order", "ChangedByUser"],
    ),
    Table(
        "Fratak_Shop.Data.Entities.Marketing.CouponUsage", "CouponUsages",
        "Marketing/Coupon.cs", "CouponConfiguration.cs",
        skip={"Coupon", "User", "Order"},
        indexes=[(("CouponId",), False), (("OrderId",), False)],
        fks=[
            ("CouponId", COUPONS, "Id", "Coupon", "Usages", "Cascade"),
            ("UserId", USERS, "Id", "User", None, "SetNull"),
            ("OrderId", ORDERS, "Id", "Order", None, "Cascade"),
        ],
        navigations=["Coupon", "User", "Order"],
    ),
    Table(
        "Fratak_Shop.Data.Entities.Notifications.Notification", "Notifications",
        "Notifications/Notification.cs", "NotificationConfiguration.cs",
        skip={"User", "CreatedByUser"},
        indexes=[(("UserId", "IsRead"), False), (("Type",), False)],
        fks=[
            ("UserId", USERS, "Id", "User", "Notifications", "Cascade"),
            ("CreatedByUserId", USERS, "Id", "CreatedByUser", None, "SetNull"),
        ],
        navigations=["User", "CreatedByUser"],
    ),
    Table(
        "Fratak_Shop.Data.Entities.Content.ContentPage", "ContentPages",
        "Content/ContentPage.cs", "ContentPageConfiguration.cs",
        indexes=[(("Page",), True)],
    ),
    Table(
        "Fratak_Shop.Data.Entities.Seo.SeoAuditRecord", "SeoAuditRecords",
        "Seo/SeoAuditRecord.cs", "SeoAuditRecordConfiguration.cs",
        skip={"AppliedByUser", "CreatedByUser"},
        indexes=[(("EntityType", "EntityId"), False), (("Status",), False)],
        fks=[
            ("AppliedByUserId", USERS, "Id", "AppliedByUser", None, "SetNull"),
            ("CreatedByUserId", USERS, "Id", "CreatedByUser", None, "SetNull"),
        ],
        navigations=["AppliedByUser", "CreatedByUser"],
    ),
    Table(
        "Fratak_Shop.Data.Entities.Seo.NotFoundLog", "NotFoundLogs",
        "Seo/NotFoundLog.cs", "NotFoundLogConfiguration.cs",
        indexes=[(("Path",), True), (("IsResolved",), False)],
    ),
    Table(
        "Fratak_Shop.Data.Entities.Seo.ContentVisit", "ContentVisits",
        "Seo/ContentVisit.cs", "ContentVisitConfiguration.cs",
        indexes=[(("EntityType", "EntityId", "VisitDate"), True), (("VisitDate",), False)],
    ),
    Table(
        "Fratak_Shop.Data.Entities.Audit.AuditLog", "AuditLogs",
        "Audit/AuditLog.cs", "AuditLogConfiguration.cs",
        skip={"User"},
        indexes=[(("UserId",), False), (("Action",), False), (("EntityType", "EntityId"), False)],
        fks=[("UserId", USERS, "Id", "User", None, "SetNull")],
        navigations=["User"],
    ),
]

# ---------------------------------------------------------------------------
# new columns of existing tables: (table, clr, property, default)
# ---------------------------------------------------------------------------
EXISTING_TABLE_CHANGES = [
    ("Fratak_Shop.Data.Entities.Users.User", "Users", "IsActive", True),
    ("Fratak_Shop.Data.Entities.Users.User", "Users", "AvatarImageName", None),
    ("Fratak_Shop.Data.Entities.Products.Product", "Products", "IsFeatured", None),
    ("Fratak_Shop.Data.Entities.Products.Product", "Products", "ViewCount", None),
    ("Fratak_Shop.Data.Entities.Blogs.BlogPost", "BlogPosts", "IsFeatured", None),
    ("Fratak_Shop.Data.Entities.Orders.Order", "Orders", "TrackingUrl", None),
    ("Fratak_Shop.Data.Entities.Orders.Order", "Orders", "CouponId", None),
    ("Fratak_Shop.Data.Entities.Orders.Order", "Orders", "ShippingMethodId", None),
    ("Fratak_Shop.Data.Entities.Orders.Order", "Orders", "AdminNote", None),
    ("Fratak_Shop.Data.Entities.Orders.Order", "Orders", "CancelledAt", None),
    ("Fratak_Shop.Data.Entities.Orders.Order", "Orders", "CancelReason", None),
    ("Fratak_Shop.Data.Entities.Marketing.Banner", "Banners", "Position", 1),
    ("Fratak_Shop.Data.Entities.Marketing.Banner", "Banners", "ViewCount", None),
    ("Fratak_Shop.Data.Entities.Marketing.Banner", "Banners", "ClickCount", None),
    ("Fratak_Shop.Data.Entities.Messages.Message", "Messages", "Reply", None),
    ("Fratak_Shop.Data.Entities.Messages.Message", "Messages", "RepliedByUserId", None),
    ("Fratak_Shop.Data.Entities.Shopping.Cart", "Carts", "CouponCode", None),
    ("Fratak_Shop.Data.Entities.SeoRedirects.SeoRedirect", "SeoRedirects", "HitCount", None),
    ("Fratak_Shop.Data.Entities.SeoRedirects.SeoRedirect", "SeoRedirects", "LastHitAt", None),
]

# new foreign keys / indexes on existing tables
EXISTING_EXTRA_INDEXES = [
    ("Users", ("Role",), False),
    ("Users", ("IsActive",), False),
    ("Orders", ("CouponId",), False),
    ("Orders", ("ShippingMethodId",), False),
    ("Messages", ("RepliedByUserId",), False),
    ("Carts", ("UpdatedAt",), False),
]

EXISTING_EXTRA_FKS = [
    ("Orders", "CouponId", COUPONS, "SetNull", "FK_Orders_Coupons_CouponId"),
    ("Orders", "ShippingMethodId", "Fratak_Shop.Data.Entities.Orders.ShippingMethod", "SetNull",
     "FK_Orders_ShippingMethods_ShippingMethodId"),
    ("Messages", "RepliedByUserId", USERS, "SetNull", "FK_Messages_Users_RepliedByUserId"),
]

# new owned (SeoMetaData) columns: table name -> owner clr
OWNED_SEO_TABLES = [
    ("BlogCategories", "Fratak_Shop.Data.Entities.Blogs.BlogCategory"),
    ("BlogPosts", "Fratak_Shop.Data.Entities.Blogs.BlogPost"),
    ("Categories", "Fratak_Shop.Data.Entities.Categories.Category"),
    ("PageSeos", "Fratak_Shop.Data.Entities.Pages.PageSeo"),
    ("Products", "Fratak_Shop.Data.Entities.Products.Product"),
]
NEW_SEO_PROPS = [
    ("SecondaryKeywordsJson", "string", True, False, None, 0),
    ("HreflangJson", "string", True, False, None, 0),
    ("SeoScore", "int", True, False, None, 0),
    ("LastAuditedAt", "DateTimeOffset", True, False, None, 0),
]


def find_entity_prop(clr: str, name: str) -> Prop:
    for table in TABLES:
        if table.clr == clr:
            return table.prop(name)
    # existing entity
    namespace, class_name = clr.rsplit(".", 1)
    folder = class_name
    for path in ENTITIES.rglob(f"{class_name}.cs"):
        props = with_base_entity(parse_entity(path, set()))
        for prop in props:
            if prop.name == name:
                return prop
    raise KeyError(f"{name} not found on {clr}")


def migration_column(prop: Prop, indent: str) -> str:
    for_cs = prop.clr + ("?" if prop.nullable and prop.clr not in ("string", "byte[]") else "")
    parts = [f'type: "{prop.column_type}"']
    if prop.max_length:
        parts.append(f"maxLength: {prop.max_length}")
    parts.append(f"nullable: {'true' if prop.nullable else 'false'}")
    return f"{indent}{prop.name} = table.Column<{for_cs}>({', '.join(parts)}),"


def snapshot_property(prop: Prop, indent: str) -> str:
    cs = prop.clr + ("?" if prop.nullable else "")
    lines = [f'{indent}b.Property<{cs}>("{prop.name}")']
    inner = indent + "    "
    if prop.name == "Id":
        lines.append(f"{inner}.ValueGeneratedOnAdd()")
    if not prop.nullable and prop.is_string:
        lines.append(f"{inner}.IsRequired()")
    if prop.max_length:
        lines.append(f"{inner}.HasMaxLength({prop.max_length})")
    if prop.is_decimal and prop.precision is not None:
        lines.append(f"{inner}.HasPrecision(18)" if prop.precision == 0 else f"{inner}.HasPrecision(18, {prop.precision})")
    lines.append(f'{inner}.HasColumnType("{prop.column_type}");')
    return "\n".join(lines)


def ref_action(on_delete: str) -> str | None:
    return {
        "Cascade": "ReferentialAction.Cascade",
        "SetNull": "ReferentialAction.SetNull",
        "Restrict": "ReferentialAction.Restrict",
        "NoAction": "ReferentialAction.NoAction",
    }.get(on_delete)


def short_name(clr: str) -> str:
    return clr.split(".")[-1]


# ---------------------------------------------------------------------------
# migration emitter
# ---------------------------------------------------------------------------
def emit_create_table(table: Table, indent: str = "            ") -> str:
    i = indent
    body = [f'{i}migrationBuilder.CreateTable(', f'{i}    name: "{table.table}",',
            f'{i}    columns: table => new', f'{i}    {{']
    for prop in sorted(table.props, key=lambda p: (p.name != "Id", p.name)):
        body.append(migration_column(prop, i + "        "))
    body.append(f'{i}    }},')
    body.append(f'{i}    constraints: table =>')
    body.append(f'{i}    {{')
    body.append(f'{i}        table.PrimaryKey("PK_{table.table}", x => x.Id);')
    for fk in table.fks:
        prop, principal, principal_prop, nav, withmany, on_delete, *rest = fk
        constraint = f'FK_{table.table}_{principal_table(principal)}_{prop}'
        if prop == "OrderId" and len(rest) and rest[0] == "one-to-one":
            constraint = f'FK_{table.table}_{principal_table(principal)}_{prop}'
        body.append(f'{i}        table.ForeignKey(')
        body.append(f'{i}            name: "{constraint}",')
        body.append(f'{i}            column: x => x.{prop},')
        body.append(f'{i}            principalTable: "{principal_table(principal)}",')
        body.append(f'{i}            principalColumn: "{principal_prop}",')
        if on_delete in ("NoAction", None):
            body[-1] = body[-1] + ");"
        else:
            body.append(f'{i}            onDelete: {ref_action(on_delete)});')
    body.append(f'{i}    }});')
    return "\n".join(body) + "\n"


def principal_table(clr: str) -> str:
    mapping = {
        USERS: "Users",
        ORDERS: "Orders",
        COUPONS: "Coupons",
        "Fratak_Shop.Data.Entities.Navigation.MenuItem": "MenuItems",
        "Fratak_Shop.Data.Entities.Payments.PaymentCard": "PaymentCards",
        "Fratak_Shop.Data.Entities.Payments.BankTransferPayment": "BankTransferPayments",
        "Fratak_Shop.Data.Entities.Orders.ShippingMethod": "ShippingMethods",
    }
    if clr in mapping:
        return mapping[clr]
    raise KeyError(clr)


def index_name(table: str, props: tuple[str, ...], unique: bool) -> str:
    name = "_".join(props)
    return f"IX_{table}_{name}"


def emit_index(table: str, props, unique, indent="            ", sort_order=0) -> str:
    i = indent
    label = "column" if len(props) == 1 else "columns"
    lines = [f'{i}migrationBuilder.CreateIndex(', f'{i}    name: "{index_name(table, props, unique)}",',
             f'{i}    table: "{table}",',
             f'{i}    {label}: {props_to_lambda(props)},']
    if unique:
        lines.append(f'{i}    unique: true,')
        lines.append(f'{i}    filter: "[IsDeleted] = 0");')
    else:
        lines[-1] = lines[-1][:-1] + ");"
    return "\n".join(lines) + "\n"


def props_to_lambda(props) -> str:
    if len(props) == 1:
        return f'"{props[0]}"'
    inner = ", ".join(f'"{p}"' for p in props)
    return f"new[] {{ {inner} }}"


def build_up() -> str:
    out = []
    # 1) new columns on existing tables
    for clr, table_name, prop_name, default in EXISTING_TABLE_CHANGES:
        prop = find_entity_prop(clr, prop_name)
        if default is None and not prop.nullable and not prop.is_string:
            default = False if prop.cs_type == "bool" else 0
        cs = prop.clr + ("?" if prop.nullable and prop.clr not in ("string",) else "")
        args = [f'name: "{prop.name}"', f'table: "{table_name}"', f'type: "{prop.column_type}"']
        if prop.max_length:
            args.append(f"maxLength: {prop.max_length}")
        if prop.is_decimal:
            args.append("precision: 18")
            args.append("scale: 0")
        args.append(f"nullable: {'true' if prop.nullable else 'false'}")
        if default is not None:
            if isinstance(default, bool):
                args.append(f"defaultValue: {'true' if default else 'false'}")
            else:
                args.append(f"defaultValue: {default}")
        out.append("            migrationBuilder.AddColumn<%s>(\n                %s);\n" % (cs, ",\n                ".join(args)))

    # 2) new tables (dependency order as declared in TABLES)
    for table in TABLES:
        out.append(emit_create_table(table))

    # 3) owned SEO columns of the existing tables
    for table_name, _ in OWNED_SEO_TABLES:
        for prop_name, cs_type, nullable, required, max_length, precision in NEW_SEO_PROPS:
            prop = Prop(prop_name, cs_type, nullable, required, max_length, precision)
            cs = prop.clr + ("?" if prop.nullable and prop.clr not in ("string",) else "")
            out.append(
                "            migrationBuilder.AddColumn<%s>(\n"
                '                name: "Seo_%s",\n'
                '                table: "%s",\n'
                '                type: "%s",\n'
                "                nullable: true);\n" % (cs, prop.name, table_name, prop.column_type)
            )

    # 4) indexes
    for table in TABLES:
        for props, unique in table.indexes:
            out.append(emit_index(table.table, props, unique))
    for table_name, props, unique in EXISTING_EXTRA_INDEXES:
        out.append(emit_index(table_name, props, unique))

    # 5) foreign keys on existing tables
    for table_name, prop_name, principal, on_delete, constraint in EXISTING_EXTRA_FKS:
        out.append(
            "            migrationBuilder.AddForeignKey(\n"
            f'                name: "{constraint}",\n'
            f'                table: "{table_name}",\n'
            f'                column: "{prop_name}",\n'
            f'                principalTable: "{principal_table(principal)}",\n'
            f'                principalColumn: "Id",\n'
            f"                onDelete: {ref_action(on_delete)});\n"
        )
    return "\n".join(out)


def build_down() -> str:
    out = []
    for table_name, prop_name, principal, on_delete, constraint in EXISTING_EXTRA_FKS:
        out.append(
            "            migrationBuilder.DropForeignKey(\n"
            f'                name: "{constraint}",\n'
            f'                table: "{table_name}");\n'
        )
    for table_name, props, unique in EXISTING_EXTRA_INDEXES:
        out.append(
            "            migrationBuilder.DropIndex(\n"
            f'                name: "{index_name(table_name, props, unique)}",\n'
            f'                table: "{table_name}");\n'
        )
    for table in reversed(TABLES):
        for props, unique in reversed(table.indexes):
            out.append(
                "            migrationBuilder.DropIndex(\n"
                f'                name: "{index_name(table.table, props, unique)}",\n'
                f'                table: "{table.table}");\n'
            )
    for table_name, _ in OWNED_SEO_TABLES:
        for prop_name, *_ in NEW_SEO_PROPS:
            out.append(
                "            migrationBuilder.DropColumn(\n"
                f'                name: "Seo_{prop_name}",\n'
                f'                table: "{table_name}");\n'
            )
    for table in reversed(TABLES):
        out.append(
            "            migrationBuilder.DropTable(\n"
            f'                name: "{table.table}");\n'
        )
    for clr, table_name, prop_name, default in reversed(EXISTING_TABLE_CHANGES):
        prop = find_entity_prop(clr, prop_name)
        out.append(
            "            migrationBuilder.DropColumn(\n"
            f'                name: "{prop.name}",\n'
            f'                table: "{table_name}");\n'
        )
    return "\n".join(out)


MIGRATION_TEMPLATE = '''using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fratak_Shop.Data.Migrations
{{
    /// <inheritdoc />
    public partial class {name} : Migration
    {{
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {{
{up}        }}

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {{
{down}        }}
    }}
}}
'''


# ---------------------------------------------------------------------------
# snapshot emitter
# ---------------------------------------------------------------------------
def snapshot_entity_block(table: Table) -> str:
    i = "            "
    j = i + "    "
    k = j + "    "
    l = k + "    "
    lines = [f'{i}modelBuilder.Entity("{table.clr}", b =>', f'{j}{{']
    for prop in sorted(table.props, key=lambda p: (p.name != "Id", p.name)):
        lines.append(snapshot_property(prop, k))
        lines.append("")
    lines.append(f'{k}b.HasKey("Id");')
    lines.append("")
    for props, unique in table.indexes:
        if len(props) == 1:
            lines.append(f'{k}b.HasIndex("{props[0]}")')
        else:
            inner = ", ".join(f'"{p}"' for p in props)
            lines.append(f'{k}b.HasIndex({inner})')
        if unique:
            lines.append(f'{l}.IsUnique()')
            lines.append(f'{l}.HasFilter("[IsDeleted] = 0");')
        else:
            lines[-1] = lines[-1] + ";"
        lines.append("")
    if table.checks:
        lines.append(f'{k}b.ToTable("{table.table}", t =>')
        lines.append(f'{l}{{')
        for check_name, check_body in table.checks:
            escaped = check_body.replace('"', '\\"')
            lines.append(f'{l}    t.HasCheckConstraint("{check_name}", "{escaped}");')
        lines.append(f'{l}}});')
    else:
        lines.append(f'{k}b.ToTable("{table.table}");')
    lines.append("")
    for fk in table.fks:
        prop, principal, principal_prop, nav, withmany, on_delete, *rest = fk
        if withmany:
            lines.append(f'{k}b.HasOne("{principal}", "{nav}")')
            lines.append(f'{l}.WithMany("{withmany}")')
        elif len(rest) and rest[0] == "one-to-one":
            lines.append(f'{k}b.HasOne("{principal}", "{nav}")')
            lines.append(f'{l}.WithOne("{nav}")')
        else:
            lines.append(f'{k}b.HasOne("{principal}", "{nav}")')
            lines.append(f'{l}.WithMany()')
        if len(rest) and rest[0] == "one-to-one":
            lines.append(f'{l}.HasForeignKey("{table.clr}", "{prop}")')
        else:
            lines.append(f'{l}.HasForeignKey("{prop}")')
        lines.append(f'{l}.OnDelete(DeleteBehavior.{on_delete});')
        lines.append("")
    for nav in table.navigations:
        lines.append(f'{k}b.Navigation("{nav}");')
    lines.append(f'{j}}});')
    return "\n".join(lines) + "\n"


def tablename_for(clr: str) -> str:
    return short_name(clr)


def block_bounds(text: str, clr: str, needle: str | None = None, need_key: bool = False):
    """Returns (start, end) of an entity block; `end` points right after `});`.

    When `needle` is given the block containing that snippet is returned (an
    entity can be described by several blocks: properties, relationships and
    navigations are split by EF when they were added later)."""
    search_from = 0
    while True:
        at = text.find(f'modelBuilder.Entity("{clr}", b =>', search_from)
        if at < 0:
            return None
        open_brace = text.find("{", at)
        depth = 0
        cursor = open_brace
        while cursor < len(text):
            if text[cursor] == "{":
                depth += 1
            elif text[cursor] == "}":
                depth -= 1
                if depth == 0:
                    break
            cursor += 1
        close = text.find(");", cursor)
        block_end = close + 2
        block = text[at:block_end]
        if needle is not None:
            if needle in block:
                return at, block_end
        elif not need_key or 'b.HasKey("Id");' in block:
            return at, block_end
        search_from = block_end
        if search_from >= len(text):
            return None


def main():
    snapshot = SNAPSHOT.read_text(encoding="utf-8")

    # ---------------------------------------------------------------- migration
    migration_text = MIGRATION_TEMPLATE.format(name=MIGRATION_NAME, up=build_up(), down=build_down())
    migration_path = MIGRATIONS / f"{STAMP}_{MIGRATION_NAME}.cs"
    migration_path.write_text(migration_text, encoding="utf-8")

    # ---------------------------------------------------------------- snapshot
    new_blocks = "\n".join(snapshot_entity_block(table) for table in TABLES)
    index = snapshot.rfind("\n        }\n    }\n}")
    assert index > 0, "could not find the end of BuildModel"
    snapshot = snapshot[:index] + "\n" + new_blocks + snapshot[index:]

    # owned SEO properties inside the five owned blocks
    for table_name, owner_clr in OWNED_SEO_TABLES:
        bounds = block_bounds(snapshot, owner_clr, needle="b.OwnsOne(")
        assert bounds, f"OwnsOne block not found for {table_name}"
        block_start, block_end = bounds
        own_index = snapshot.find("b.OwnsOne(", block_start, block_end)
        # end of the owns-one block: the first "});" at its indentation level
        close = snapshot.find("\n                        });", own_index)
        assert 0 < close < block_end, f"end of OwnsOne not found for {table_name}"
        insert_at = snapshot.rfind("b1.HasKey(", own_index, close)
        assert insert_at > 0
        chunks = []
        for prop_name, cs_type, nullable, required, max_length, precision in NEW_SEO_PROPS:
            prop = Prop(prop_name, cs_type, nullable, required, max_length, precision)
            chunk = snapshot_property(prop, " " * 28).replace("b.Property<", "b1.Property<")
            chunks.append(chunk + "\n\n")
        snapshot = snapshot[:insert_at] + "".join(chunks) + " " * 28 + snapshot[insert_at:]

    def insert_in_block(clr: str, text: str, before: str | None = None):
        """Inserts `text` (already indented, ending with a newline) inside the block
        of `clr` — either right before `before` or at the end of the block."""
        nonlocal snapshot
        bounds = block_bounds(snapshot, clr, needle='b.HasKey("Id");')
        assert bounds, clr
        block_start, block_end = bounds
        if before:
            at = snapshot.find(before, block_start, block_end)
            assert at > 0, f"{before} not found in {clr}"
            at = snapshot.rfind("\n", block_start, at) + 1
        else:
            closing = snapshot.rfind("});", block_start, block_end)
            assert closing > 0
            at = snapshot.rfind("\n", block_start, closing) + 1
        snapshot = snapshot[:at] + text + snapshot[at:]

    # new columns inside the existing entity blocks
    for clr, table_name, prop_name, default in EXISTING_TABLE_CHANGES:
        prop = find_entity_prop(clr, prop_name)
        insert_in_block(clr, snapshot_property(prop, " " * 20) + "\n\n", before='b.HasKey("Id");')

    # new indexes of the existing tables
    for table_name, props, unique in EXISTING_EXTRA_INDEXES:
        clr = next(c for c, t, p, d in EXISTING_TABLE_CHANGES if t == table_name)
        if len(props) == 1:
            text = f'b.HasIndex("{props[0]}");'
        else:
            text = "b.HasIndex(" + ", ".join(f'"{p}"' for p in props) + ");"
        insert_in_block(clr, " " * 20 + text + "\n\n", before='b.ToTable("')

    # new relationships of the existing entities
    relationship_chunks = {
        ORDERS: [
            ("Fratak_Shop.Data.Entities.Marketing.Coupon", "Coupon", None, "CouponId"),
            ("Fratak_Shop.Data.Entities.Orders.ShippingMethod", "ShippingMethod", None, "ShippingMethodId"),
        ],
        "Fratak_Shop.Data.Entities.Messages.Message": [
            (USERS, "RepliedByUser", None, "RepliedByUserId"),
        ],
    }
    for clr, relations in relationship_chunks.items():
        chunks = []
        for principal, nav, withmany, fk_prop in relations:
            lines = [f'b.HasOne("{principal}", "{nav}")']
            lines.append(f'.WithMany({chr(34) + withmany + chr(34) if withmany else ""})')
            lines.append(f'.HasForeignKey("{fk_prop}")')
            lines.append(".OnDelete(DeleteBehavior.SetNull);")
            chunks.append(" " * 20 + ("\n" + " " * 24).join(lines) + "\n\n")
        insert_in_block(clr, "".join(chunks))

    # navigations of the existing entities that gained a new navigation property
    navigation_additions = {
        ORDERS: ["Coupon", "ShippingMethod", "StatusHistory", "PaymentTransactions", "BankTransferPayment"],
        "Fratak_Shop.Data.Entities.Messages.Message": ["RepliedByUser"],
    }
    for clr, navs in navigation_additions.items():
        bounds = block_bounds(snapshot, clr, needle='b.HasKey("Id");')
        assert bounds, clr
        block_start, block_end = bounds
        existing = re.findall(r'b\.Navigation\("(\w+)"\);', snapshot[block_start:block_end])
        missing = [nav for nav in navs if nav not in existing]
        if not missing:
            continue
        chunks = "".join(" " * 20 + f'b.Navigation("{nav}");\n' for nav in missing)
        insert_in_block(clr, chunks)

    # sanity check: the file still ends with the canonical closing braces
    tail = [line for line in snapshot.rstrip().splitlines() if line.strip()][-3:]
    assert tail == ["        }", "    }", "}"], tail
    assert snapshot.count("{") == snapshot.count("}"), "unbalanced braces"

    SNAPSHOT.write_text(snapshot, encoding="utf-8")

    # ---------------------------------------------------------------- designer
    body_start = snapshot.find("protected override void BuildModel(ModelBuilder modelBuilder)")
    body_start = snapshot.find("{", body_start) + 1
    body_end = snapshot.rfind("\n        }")
    model_body = snapshot[body_start:body_end]
    designer = f'''// <auto-generated />
using System;
using Fratak_Shop.Data.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace Fratak_Shop.Data.Migrations
{{
    [DbContext(typeof(FratakShopDbContext))]
    [Migration("{STAMP}_{MIGRATION_NAME}")]
    partial class {MIGRATION_NAME}
    {{
        /// <inheritdoc />
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {{
#pragma warning disable 612, 618
{model_body}
#pragma warning restore 612, 618
        }}
    }}
}}
'''
    (MIGRATIONS / f"{STAMP}_{MIGRATION_NAME}.Designer.cs").write_text(designer, encoding="utf-8")
    print(f"migration written: {migration_path.name}")
    print(f"designer written:  {STAMP}_{MIGRATION_NAME}.Designer.cs")
    print("snapshot updated")


if __name__ == "__main__":
    main()
