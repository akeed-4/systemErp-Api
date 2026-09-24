using ERP.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

[Route("api/v1/productcategories"), RequireScreen("master-data")]
public class ProductCategoriesController : CrudController<ProductCategoryDto, CreateProductCategoryDto, UpdateProductCategoryDto>
{
    public ProductCategoriesController(IProductCategoryService s) : base(s) { }
}
