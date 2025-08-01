using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using SimplCommerce.Module.Inventory.Data;
using SimplCommerce.Module.Inventory.Models;
using Xunit;

namespace SimplCommerce.Module.Inventory.Tests
{
    public class InventoryCustomModelBuilderTests
    {
        [Fact]
        public void Build_ConfiguresRowVersionAsConcurrencyToken()
        {
            var modelBuilder = new ModelBuilder();
            var builder = new InventoryCustomModelBuilder();
            builder.Build(modelBuilder);

            var entity = modelBuilder.Model.FindEntityType(typeof(Stock));
            var property = entity.FindProperty(nameof(Stock.RowVersion));

            Assert.True(property.IsConcurrencyToken);
            Assert.Equal(ValueGenerated.OnAddOrUpdate, property.ValueGenerated);
        }
    }
}
