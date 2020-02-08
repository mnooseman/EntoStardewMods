using System;
using System.Collections.Generic;
using System.Reflection;
using Entoarox.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using SDVObject = StardewValley.Object;

namespace Entoarox.ShopExpander
{
    /// <summary>The mod entry class.</summary>
    internal class ModEntry : Mod
    {
        /*********
        ** Fields
        *********/
        private static readonly Dictionary<string, SObject> AddedObjects = new Dictionary<string, SObject>();
        private static readonly Dictionary<string, Item> ReplacementStacks = new Dictionary<string, Item>();
        private ModConfig Config;
        private bool EventsActive;
        private byte SkippedTicks;
        private readonly List<string> AffectedShops = new List<string>();


        /*********
        ** Public methods
        *********/
        /// <summary>The mod entry point, called after the mod is first loaded.</summary>
        /// <param name="helper">Provides simplified APIs for writing mods.</param>
        public override void Entry(IModHelper helper)
        {
            helper.Events.Display.MenuChanged += this.OnMenuChanged;
            helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
        }


        /*********
        ** Protected methods
        *********/
        /// <summary>Raised after the game state is updated (≈60 times per second).</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void OnUpdateTicked(object sender, EventArgs e)
        {
            if (this.SkippedTicks > 1)
            {
                this.Config = this.Helper.ReadConfig<ModConfig>();

                this.Helper.Events.GameLoop.UpdateTicked -= this.OnUpdateTicked;
                foreach (Reference obj in this.Config.Objects)
                {
                    try
                    {
                        this.GenerateObject(obj.Owner, obj.Item, obj.Amount, obj.Conditions);
                    }
                    catch (Exception err)
                    {
                        this.Monitor.Log($"Object failed to generate: {obj}", LogLevel.Error, err);
                    }
                }
            }
            else
                this.SkippedTicks++;
        }

        private void GenerateObject(string owner, int replacement, int stackAmount, string requirements)
        {
            if (owner == "???")
            {
                this.Monitor.Log("Attempt to add a object to a shop owned by `???`, this cant be done because `???` means the owner is unknown!", LogLevel.Error);
                return;
            }

            SDVObject stack = new SDVObject(replacement, stackAmount);
            if (stack.salePrice() == 0)
            {
                this.Monitor.Log("Unable to add item to shop, it has no value: " + replacement, LogLevel.Error);
                return;
            }

            SObject obj = new SObject(stack, stackAmount)
            {
                targetedShop = owner,
                requirements = requirements
            };

            if (!this.AffectedShops.Contains(owner))
                this.AffectedShops.Add(owner);

            if (!ModEntry.AddedObjects.ContainsKey(obj.Name))
            {
                ModEntry.AddedObjects.Add(obj.Name, obj);
                ModEntry.ReplacementStacks.Add(obj.Name, stack);
            }
        }

        /// <summary>Raised after items are added or removed to a player's inventory.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void OnInventoryChanged(object sender, InventoryChangedEventArgs e)
        {
            // If the inventory changes while this even is hooked, we need to check if any SObject instances are in it, so we can replace them
            if (e.IsLocalPlayer)
            {
                for (int c = 0; c < Game1.player.Items.Count; c++)
                {
                    if (Game1.player.Items[c] is SObject obj)
                    {
                        this.Monitor.Log($"Reverting object: {obj.Name}:{obj.Stack}", LogLevel.Trace);
                        Game1.player.Items[c] = obj.Revert();
                    }
                }
            }
        }

        // Add a modified "stack" item to the shop
        private void AddItem(ShopMenu menu, SObject item, string location)
        {
            // Check that makes sure only the items that the current shop is supposed to sell are added
            if (location != item.targetedShop)
            {
                this.Monitor.Log("Item(" + item.Name + ':' + item.stackAmount + '*' + item.maximumStackSize() + "){Location=false}", LogLevel.Trace);
                return;
            }

            if (!string.IsNullOrEmpty(item.requirements) && !this.Helper.Conditions().ValidateConditions(item.requirements))
            {
                this.Monitor.Log("Item(" + item.Name + ':' + item.stackAmount + '*' + item.maximumStackSize() + "){Location=true,Condition=false}", LogLevel.Trace);
                return;
            }

            if (item.stackAmount == 1)
            {
                this.Monitor.Log("Item(" + item.Name + ':' + item.stackAmount + '*' + item.maximumStackSize() + "){Location=true,Condition=true,Stack=false}", LogLevel.Trace);
                Item reverted = item.Revert();
                menu.forSale.Add(reverted);
                menu.itemPriceAndStock.Add(reverted, new int[2] { reverted.salePrice(), int.MaxValue });
            }
            else
            {
                this.Monitor.Log("Item(" + item.Name + ':' + item.stackAmount + '*' + item.maximumStackSize() + "){Location=true,Condition=true,Stack=true}", LogLevel.Trace);
                menu.forSale.Add(item);
                menu.itemPriceAndStock.Add(item, new int[2] { item.salePrice(), int.MaxValue });
            }
        }

        /// <summary>Raised after a game menu is opened, closed, or replaced.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void OnMenuChanged(object sender, MenuChangedEventArgs e)
        {
            // when the menu closes, remove the hook for the inventory changed event
            if (e.OldMenu is ShopMenu && this.EventsActive)
            {
                this.Helper.Events.Player.InventoryChanged -= this.OnInventoryChanged;
            }

            // check if the current menu is that for a shop
            if (e.NewMenu is ShopMenu menu)
            {
                // When it is a shop menu, I need to perform some logic to identify the HatMouse, Traveler or ClintUpgrade shops as I cannot simply use their owner for that
                this.Monitor.Log("Shop Menu active, checking for expansion", LogLevel.Trace);
                string shopOwner = "???";
                // There are by default two shops in the forest, and neither has a owner, so we need to manually resolve the shop owner
                if (menu.portraitPerson != null)
                {
                    shopOwner = menu.portraitPerson.Name;
                    // Clint has two shops, we need to check if this is the tool upgrade shop and modify the owner if that is the case
                    if (shopOwner == "Clint" && menu.potraitPersonDialogue == "I can upgrade your tools with more power. You'll have to leave them with me for a few days, though.")
                        shopOwner = "ClintUpgrade";
                }
                else
                {
                    switch (Game1.currentLocation.Name)
                    {
                        case "Forest":
                            if (menu.potraitPersonDialogue == "Hiyo, poke. Did you bring coins? Gud. Me sell hats.")
                                shopOwner = "HatMouse";
                            else
                            {
                                // The merchant is a bit harder to determine then the mouse
                                List<string> matches = new List<string>
                                {
                                    "I've got a little bit of everything. Take a look!",
                                    "I smuggled these goods out of the Gotoro Empire. Why do you think they're so expensive?",
                                    "I'll have new items every week, so make sure to come back!",
                                    "Beautiful country you have here. One of my favorite stops. The pig likes it, too.",
                                    "Let me see... Oh! I've got just what you need: "
                                };
                                // We only set the owner if it actually is the traveler, so custom unowned shops will simply remain unidentified
                                if (matches.Contains(menu.potraitPersonDialogue) || menu.potraitPersonDialogue.Substring(0, matches[4].Length) == matches[4])
                                    shopOwner = "Traveler";
                            }

                            break;
                        case "Hospital":
                            shopOwner = "Hospital";
                            break;
                        case "Club":
                            shopOwner = "MisterQi";
                            break;
                        case "JojaMart":
                            shopOwner = "Joja";
                            break;
                    }
                }
                if (this.AffectedShops.Contains(shopOwner))
                {
                    this.Monitor.Log($"Shop owned by `{shopOwner}` gets modified, doing so now", LogLevel.Trace);
                    // Register to inventory changes so we can immediately replace bought stacks
                    this.EventsActive = true;
                    this.Helper.Events.Player.InventoryChanged += this.OnInventoryChanged;
                    // Add our custom items to the shop
                    foreach (string key in ModEntry.AddedObjects.Keys)
                        this.AddItem(menu, ModEntry.AddedObjects[key], shopOwner);
                    // Use reflection to set the changed values
                }
                else
                {
                    if (shopOwner.Equals("???"))
                        this.Monitor.Log("The shop owner could not be resolved, skipping shop", LogLevel.Trace);
                    else
                        this.Monitor.Log($"The shop owned by `{shopOwner}` is not on the list, ignoring it");
                }
            }
        }
    }
}
