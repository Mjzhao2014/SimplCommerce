using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Moq;
using MockQueryable.Moq;
using SimplCommerce.Infrastructure.Data;
using SimplCommerce.Module.Catalog.Models;
using SimplCommerce.Module.Checkouts.Models;
using SimplCommerce.Module.Core.Models;
using SimplCommerce.Module.Orders.Models;
using SimplCommerce.Module.Orders.Services;
using SimplCommerce.Module.Pricing.Services;
using SimplCommerce.Module.ShippingPrices.Models;
using SimplCommerce.Module.ShippingPrices.Services;
using SimplCommerce.Module.Tax.Services;

namespace SimplCommerce.Module.Orders.Tests;

public class OrderServiceConcurrencyTests
{
    [Fact]
    public async Task CreateOrder_ShouldFail_WhenConcurrencyOccurs()
    {
        var product = new Product
        {
            IsAllowToOrder = true,
            IsPublished = true,
            StockTrackingIsEnabled = true,
            StockQuantity = 5,
            Price = 10
        };
        typeof(Product).GetProperty("Id")!.SetValue(product, 1L);

        var checkoutItem = new CheckoutItem { Product = product, Quantity = 3 };
        var checkout = new Checkout { Customer = new User(), CreatedBy = new User() };
        checkout.CheckoutItems.Add(checkoutItem);
        typeof(Checkout).GetProperty("Id")!.SetValue(checkout, Guid.NewGuid());

        var checkoutQueryable = new List<Checkout> { checkout }.AsQueryable().BuildMock();
        var checkoutRepo = new Mock<IRepositoryWithTypedId<Checkout, Guid>>();
        checkoutRepo.Setup(r => r.Query()).Returns(checkoutQueryable);

        var orderRepo = new Mock<IRepository<Order>>();
        var couponService = new Mock<ICouponService>();
        couponService.Setup(c => c.Validate(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CartInfoForCoupon>()))
                     .ReturnsAsync(new CouponValidationResult { Succeeded = true });

        var checkoutItemRepo = new Mock<IRepository<CheckoutItem>>();
        var dbTransaction = new Mock<IDbContextTransaction>();
        checkoutItemRepo.Setup(r => r.BeginTransaction()).Returns(dbTransaction.Object);
        checkoutItemRepo.Setup(r => r.SaveChanges()).Throws(new DbUpdateConcurrencyException());

        var orderItemRepo = new Mock<IRepository<OrderItem>>();
        var taxService = new Mock<ITaxService>();
        taxService.Setup(t => t.GetTaxPercent(It.IsAny<long?>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>())).ReturnsAsync(0);
        var checkoutService = new Mock<ICheckoutService>();
        var shippingPriceService = new Mock<IShippingPriceService>();
        shippingPriceService.Setup(s => s.GetApplicableShippingPrices(It.IsAny<GetShippingPriceRequest>()))
                            .ReturnsAsync(new List<ShippingPrice> { new ShippingPrice { Name = "Default", Price = 0 } });
        var userAddressRepo = new Mock<IRepository<UserAddress>>();
        var mediator = new Mock<IMediator>();
        var productPricingService = new Mock<IProductPricingService>();
        productPricingService.Setup(p => p.CalculateProductPrice(product)).Returns(new CalculatedProductPrice { Price = product.Price });

        var service = new OrderService(orderRepo.Object, couponService.Object, checkoutItemRepo.Object,
            orderItemRepo.Object, taxService.Object, checkoutService.Object, checkoutRepo.Object,
            shippingPriceService.Object, userAddressRepo.Object, mediator.Object, productPricingService.Object);

        var address = new Address();

        var result = await service.CreateOrder(checkout.Id, "Cash", 0, "Default", address, address);

        Assert.False(result.Success);
    }
}

