using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Starlights.Modules.Elements.Domain.Components;

namespace Starlights.Modules.Elements.Data.EntityFramework.TypeConfiguration;

public class SupportsComponentTypeConfiguration : IEntityTypeConfiguration<SupportsComponent>
{
    public void Configure(EntityTypeBuilder<SupportsComponent> builder)
    {
        builder.ToTable("element_component_supports");

        // compared by content, like the classifications' lists: without it EF compares the list by reference
        var listComparer = new ValueComparer<List<string>>(
            (left, right) => left!.SequenceEqual(right!),
            value => value.Aggregate(0, (hash, item) => hash ^ StringComparer.Ordinal.GetHashCode(item)), value => value.ToList());

        builder.Property<List<string>>("_supports")
            .HasColumnName("supports")
            .HasColumnType("nvarchar(max)")
            .HasConversion(v => JsonSerializer.Serialize(v), v => JsonSerializer.Deserialize<List<string>>(v) ?? new List<string>())
            .Metadata.SetValueComparer(listComparer);
    }
}
