using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using SimplCommerce.Infrastructure;
using SimplCommerce.Infrastructure.Modules;
using SimplCommerce.Module.Core;
using SimplCommerce.Module.Core.Data;
using SimplCommerce.Module.Catalog;
using SimplCommerce.Module.Catalog.Models;
using SimplCommerce.Module.Inventory;
using SimplCommerce.Module.Inventory.Models;
using SimplCommerce.Module.Inventory.Services;
using Xunit;

namespace SimplCommerce.Module.Inventory.Tests
{
    public class InventoryConcurrencyTests
    {
        private static DbContextOptions<SimplDbContext> CreateOptions(string name)
        {
            return new DbContextOptionsBuilder<SimplDbContext>()
                .UseInMemoryDatabase(name)
                .Options;
        }

        private static StockService CreateService(DbContextOptions<SimplDbContext> options, IMediator mediator)
        {
            var context = new SimplDbContext(options);
            var stockRepo = new SimplCommerce.Module.Core.Data.Repository<Stock>(context);
            var productRepo = new SimplCommerce.Module.Core.Data.Repository<Product>(context);
            var historyRepo = new SimplCommerce.Module.Core.Data.Repository<StockHistory>(context);
            return new StockService(stockRepo, productRepo, historyRepo, mediator);
        }

        [Fact]
        public async Task ConcurrentOrders_TotalExceedingStock_ShouldFailOneOrder()
        {
            GlobalConfiguration.Modules = new List<ModuleInfo>
            {
                new ModuleInfo { Id = "SimplCommerce.Module.Core", Assembly = typeof(SimplCommerce.Module.Core.ModuleInitializer).Assembly },
                new ModuleInfo { Id = "SimplCommerce.Module.Catalog", Assembly = typeof(SimplCommerce.Module.Catalog.ModuleInitializer).Assembly },
                new ModuleInfo { Id = "SimplCommerce.Module.Inventory", Assembly = typeof(SimplCommerce.Module.Inventory.ModuleInitializer).Assembly }
            };

            var options = CreateOptions(Guid.NewGuid().ToString());

            using (var seed = new SimplDbContext(options))
            {
                var product = new Product { Name = "Test", Slug = "test", HasOptions = false, IsPublished = true, StockQuantity = 5 };
                typeof(Product).GetProperty("Id")!.SetValue(product, 1L);
                seed.Add(product);
                seed.Add(new Warehouse(1) { Name = "WH", AddressId = 1 });
                seed.Add(new Stock { ProductId = 1, WarehouseId = 1, Quantity = 5 });
                seed.SaveChanges();
            }

            var request = new StockUpdateRequest { ProductId = 1, WarehouseId = 1, AdjustedQuantity = -3 };

            var mediator = new Mock<IMediator>();
            var service1 = CreateService(options, mediator.Object);
            var service2 = CreateService(options, mediator.Object);

            var t1 = Task.Run(() => service1.UpdateStock(request));
            var t2 = Task.Run(() => service2.UpdateStock(request));

            var errors = await Task.WhenAll(
                t1.ContinueWith(t => t.Exception?.InnerException),
                t2.ContinueWith(t => t.Exception?.InnerException));

            Assert.Contains(errors, e => e is DbUpdateConcurrencyException);
            Assert.Contains(errors, e => e is null);

            using var verify = new SimplDbContext(options);
            var stock = await verify.Set<Stock>().FirstAsync();
            Assert.Equal(2, stock.Quantity);
        }
    }
}
