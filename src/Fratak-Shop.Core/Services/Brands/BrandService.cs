using Fratak_Shop.Core.DTOs.Brands.Command;
using Fratak_Shop.Core.DTOs.Brands.Query;
using Fratak_Shop.Core.Mappers.Brands;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Brands;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Brands;

public class BrandService(FratakShopDbContext context) : IBrandService
{
    public async Task<OperationResult> CreateBrandAsync(CreateBrandCommand command)
    {
        var error = Validate(command.Name, command.Slug);
        if (error is not null)
            return OperationResult.Error(error);

        var slug = command.Slug.Trim();

        if (await context.Brands.AnyAsync(x => x.Slug == slug))
            return OperationResult.Error("این اسلاگ قبلاً برای برند دیگری ثبت شده است.");

        context.Brands.Add(new Brand
        {
            Name = command.Name.Trim(),
            EnglishName = command.EnglishName?.Trim(),
            Slug = slug,
            LogoName = command.LogoName,
            Description = command.Description,
            DisplayOrder = command.DisplayOrder,
            IsActive = command.IsActive,
            Seo = command.Seo,
        });

        await context.SaveChangesAsync();

        return OperationResult.Success("برند با موفقیت ایجاد شد.");
    }

    public async Task<OperationResult> EditBrandAsync(EditBrandCommand command)
    {
        var brand = await context.Brands.AsTracking().FirstOrDefaultAsync(x => x.Id == command.Id);
        if (brand is null)
            return OperationResult.NotFound("برند مورد نظر یافت نشد.");

        var error = Validate(command.Name, command.Slug);
        if (error is not null)
            return OperationResult.Error(error);

        var slug = command.Slug.Trim();

        if (await context.Brands.AnyAsync(x => x.Id != command.Id && x.Slug == slug))
            return OperationResult.Error("این اسلاگ قبلاً برای برند دیگری ثبت شده است.");

        brand.Name = command.Name.Trim();
        brand.EnglishName = command.EnglishName?.Trim();
        brand.Slug = slug;
        brand.LogoName = command.LogoName;
        brand.Description = command.Description;
        brand.DisplayOrder = command.DisplayOrder;
        brand.IsActive = command.IsActive;
        brand.Seo = command.Seo;

        await context.SaveChangesAsync();

        return OperationResult.Success("برند با موفقیت ویرایش شد.");
    }

    public async Task<OperationResult> DeleteBrandAsync(Guid id)
    {
        var brand = await context.Brands.AsTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (brand is null)
            return OperationResult.NotFound("برند مورد نظر یافت نشد.");

        if (await context.Products.AnyAsync(x => x.BrandId == id))
            return OperationResult.Error("این برند محصول دارد؛ ابتدا محصولات آن را به برند دیگری منتقل کنید.");

        context.Brands.Remove(brand);
        await context.SaveChangesAsync();

        return OperationResult.Success("برند با موفقیت حذف شد.");
    }

    public async Task<List<BrandDto>> GetAllBrandsAsync()
    {
        var brands = await context.Brands
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Name)
            .ToListAsync();

        return brands.Map();
    }

    public async Task<List<BrandDto>> GetActiveBrandsAsync()
    {
        var brands = await context.Brands
            .Where(x => x.IsActive)
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Name)
            .Select(x => new
            {
                Brand = x,
                ProductCount = x.Products.Count(p => p.IsActive),
            })
            .ToListAsync();

        return brands
            .Select(x =>
            {
                var dto = x.Brand.Map();
                dto.ProductCount = x.ProductCount;
                return dto;
            })
            .ToList();
    }

    public async Task<BrandDto?> GetBrandByIdAsync(Guid id)
    {
        var brand = await context.Brands.FirstOrDefaultAsync(x => x.Id == id);
        return brand?.Map();
    }

    public async Task<BrandDto?> GetBrandBySlugAsync(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return null;

        var brand = await context.Brands.FirstOrDefaultAsync(x => x.Slug == slug.Trim());
        return brand?.Map();
    }

    private static string? Validate(string? name, string? slug)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "نام برند را وارد کنید.";

        if (string.IsNullOrWhiteSpace(slug))
            return "اسلاگ برند را وارد کنید.";

        return null;
    }
}
