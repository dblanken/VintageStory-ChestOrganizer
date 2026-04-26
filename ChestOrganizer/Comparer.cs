using System;
using System.Collections.Generic;
using Vintagestory.API.Common;

namespace ChestOrganizer;
using CompareFunc = System.Func<ItemStack, ItemStack, int>;

public class Comparer : IComparer<ItemStack> {
    public static readonly Comparer Name     = new(ByName, ByAmount);
    public static readonly Comparer Code     = new(ByCodePath, ByCodeDomain, ByAmount);
    public static readonly Comparer TypeName = new(ByType, ByName, ByAmount);

    public static Comparer CreatePerish(IWorldAccessor world, IInventory inventory)
        => new(ByPerishTime(world, inventory), ByCodePath, ByCodeDomain, ByAmount);

    private static CompareFunc ByPerishTime(IWorldAccessor world, IInventory inventory) {
        return (ItemStack x, ItemStack y) => {
            // Find current slots for these stacks by scanning inventory
            ItemSlot xSlot = null;
            ItemSlot ySlot = null;

            for (int i = 0; i < inventory.Count; i++) {
                var slot = inventory[i];
                if (slot?.Itemstack == x) xSlot = slot;
                if (slot?.Itemstack == y) ySlot = slot;
                if (xSlot != null && ySlot != null) break; // Early exit
            }

            // Get perish times for both items (handles both sealed and unsealed)
            double? xHours = GetPerishHours(world, xSlot, x);
            double? yHours = GetPerishHours(world, ySlot, y);

            // Both perishable - compare by time remaining (ascending: soonest first)
            if (xHours.HasValue && yHours.HasValue) {
                return xHours.Value.CompareTo(yHours.Value);
            }

            // Only x is perishable - x comes first
            if (xHours.HasValue) return -1;

            // Only y is perishable - y comes first
            if (yHours.HasValue) return 1;

            // Neither perishable - equal (falls through to next comparer)
            return 0;
        };
    }

    private static double? GetPerishHours(IWorldAccessor world, ItemSlot slot, ItemStack stack) {
        if (slot == null || stack == null) return null;

        var sb = new System.Text.StringBuilder();
        stack.Collectible?.GetHeldItemInfo(slot, sb, world, false);

        double? hours = PerishParser.ParseTooltip(sb.ToString());
        if (hours.HasValue)
            world.Logger.Debug($"[PerishSort] {stack.GetName()} -> {hours:F0}h ({hours/24:F1}d)");
        return hours;
    }

    private static bool CompareNullableEnum<T>(T x, T y, out int res) {
        if (x == null) {
            res = (y == null) ? 0 : -1;
            return y != null;
        } else {
            res = (x as Enum).CompareTo(y);
            return true;
        }
    }

    private static int ComparePresence<T>(T x, T y) {
        int xval = (x != null) ? 1 : 0;
        int yval = (y != null) ? 1 : 0;
        return xval - yval;
    }

    private static int ByType(ItemStack x, ItemStack y) {
        int res = x.Class.CompareTo(y.Class);
        if (res != 0) return res;
        if (x.Class == EnumItemClass.Block) {
            return x.Block.BlockMaterial.CompareTo(y.Block.BlockMaterial);
        } else {
            // WIP
            if (CompareNullableEnum(x.Item.Tool, y.Item.Tool, out res)) return res;
            return ComparePresence(x.Item.NutritionProps, y.Item.NutritionProps);
        }
    }

    private static int ByName(ItemStack x, ItemStack y)
        => x.GetName().CompareTo(y.GetName());

    private static int ByCodePath(ItemStack x, ItemStack y)
        => string.CompareOrdinal(x.Collectible.Code.Path, y.Collectible.Code.Path);

    private static int ByCodeDomain(ItemStack x, ItemStack y)
        => string.CompareOrdinal(x.Collectible.Code.Domain, y.Collectible.Code.Domain);

    private static int ByAmount(ItemStack x, ItemStack y)
        => y.StackSize.CompareTo(x.StackSize);

    private readonly CompareFunc[] comparers;

    private Comparer(params CompareFunc[] comparers) 
        => this.comparers = comparers;

    public int Compare(ItemStack x, ItemStack y) {
        if (x == null || y == null) return (y != null ? 1 : 0) - (x != null ? 1 : 0);

        int res = 0;
        foreach (var cmp in comparers) {
            res = cmp(x, y);
            if (res != 0) break;
        }
        return res;
    }
}
