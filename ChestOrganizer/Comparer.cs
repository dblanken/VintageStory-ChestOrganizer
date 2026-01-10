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

        // Try getting active transition state from the container itself
        var state = stack.Collectible?.UpdateAndGetTransitionState(world, slot, EnumTransitionType.Perish);
        if (state != null) {
            world.Logger.Debug($"[PerishSort] Container {stack.GetName()} has TransitionState: {state.FreshHoursLeft:F2}h");
            return state.FreshHoursLeft;
        }

        // world.Logger.Debug($"[PerishSort] Container {stack.GetName()} has NO TransitionState (sealed)");

        // For sealed crocks/meals, get perish time from contents
        if (stack.Attributes != null) {
            var contents = stack.Attributes.GetTreeAttribute("contents");
            if (contents != null) {
                var firstItem = contents.GetItemstack("0");
                if (firstItem != null && firstItem.Attributes != null) {
                    var foodTransState = firstItem.Attributes.GetTreeAttribute("transitionstate");
                    if (foodTransState != null) {
                        var freshHoursArray = foodTransState["freshHours"] as Vintagestory.API.Datastructures.FloatArrayAttribute;
                        var transitionHoursArray = foodTransState["transitionHours"] as Vintagestory.API.Datastructures.FloatArrayAttribute;
                        var transitionedHoursArray = foodTransState["transitionedHours"] as Vintagestory.API.Datastructures.FloatArrayAttribute;

                        if (freshHoursArray != null && freshHoursArray.value != null && freshHoursArray.value.Length > 0) {
                            // Use the MINIMUM remaining time (soonest to spoil)
                            float minFreshHours = freshHoursArray.value[0];
                            float minTransitionHours = transitionHoursArray?.value?[0] ?? 0;
                            float minTransitionedHours = transitionedHoursArray?.value?[0] ?? 0;

                            for (int i = 1; i < freshHoursArray.value.Length; i++) {
                                if (freshHoursArray.value[i] < minFreshHours) {
                                    minFreshHours = freshHoursArray.value[i];
                                    minTransitionHours = transitionHoursArray?.value?[i] ?? 0;
                                    minTransitionedHours = transitionedHoursArray?.value?[i] ?? 0;
                                }
                            }

                            world.Logger.Debug($"[PerishSort] Sealed {stack.GetName()} raw values: fresh={minFreshHours:F2}, trans={minTransitionHours:F2}, transitioned={minTransitionedHours:F2}");

                            if (minFreshHours > 0) {
                                // For sealed items, stored values are in DAYS
                                // Only use freshHours - transitionedHours (sealed items don't enter transition state)
                                double remainingDays = minFreshHours - minTransitionedHours;

                                // Get the container's perish rate for this food type
                                double transitionRate = 0.1; // Default to 0.1x (other/protein)

                                // Try to get the actual rate from the food's nutrition properties
                                if (firstItem.Collectible?.NutritionProps != null) {
                                    var foodCat = firstItem.Collectible.NutritionProps.FoodCategory;
                                    world.Logger.Debug($"[PerishSort] Food category: {foodCat}");

                                    // Container rates: Vegetable 0.08x, Grain 0.05x, Protein/Dairy/Other 0.1x
                                    if (foodCat == EnumFoodCategory.Vegetable) {
                                        transitionRate = 0.08;
                                    } else if (foodCat == EnumFoodCategory.Grain) {
                                        transitionRate = 0.05;
                                    }
                                }

                                // Divide by transition rate to get real-world days, then convert to hours
                                double realWorldDays = remainingDays / transitionRate;
                                double realWorldHours = realWorldDays * 24;

                                world.Logger.Debug($"[PerishSort] Sealed {stack.GetName()} -> {remainingDays:F2}d / {transitionRate} = {realWorldDays:F1}d ({realWorldDays/365:F1}y) = {realWorldHours:F0}h");

                                return realWorldHours;
                            }
                        }
                    }
                }
            }
        }

        // No perish information found
        return null;
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
