﻿using Eco.Core.Plugins;
using Eco.Core.Plugins.Interfaces;
using Eco.Core.Utils;
using Eco.Core.Utils.Logging;
using Eco.Gameplay.DynamicValues;
using Eco.Gameplay.Items;
using Eco.Shared.Localization;
using Eco.Shared.Utils;
using Eco.Core.PropertyHandling;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Eco.Mod.VeN.HarmonyShard;

using Eco.Gameplay.Skills;
using Eco.Gameplay.Items;
using Eco.Core.Items;
using Eco.Simulation.Agents.AI;
using Eco.ModKit;

#pragma warning disable CA1416 // Проверка совместимости платформы

namespace Eco.Mod.VeN.RecipeThrottle
{
    public class RecipeThrottle
    {
        /// Manipulete Wraping of IDynamicValue to insert Multiply coefficient to it.
        public Dictionary<RecipeFamily, int> recipeFamilyGroup;
        private PluginConfig<RecipeThrottleConfig>? pluginConfig;

        public RecipeThrottle() 
        {
            this.recipeFamilyGroup = new Dictionary<RecipeFamily, int>();
            this.pluginConfig = null;
        }

        public void Initialize (PluginConfig<RecipeThrottleConfig> pluginConfig)
        {
            this.pluginConfig=pluginConfig;
            UpdateRecipeFamilyGroup();  
        }


        public static void InsertRecipeThrottleK (IngredientElement ingredientElement , float newThrottleK = 1)
        {
            MultiDynamicValue newQuantity = WrapDynamicQuantityValue(ingredientElement.Quantity, newThrottleK);
            PrivateValueHelper.SetPrivateFieldValue<IDynamicValue>(ingredientElement, "<Quantity>k__BackingField", (IDynamicValue)newQuantity );
            ingredientElement.FirePropertyChanged("Quantity");
            //TODO Нужно выводить ошибку если неудалось
        }

        public static void RemoveRecipeThrottleK (IngredientElement ingredientElement )
        {
            IDynamicValue newQuantity = UnWrapDynamicQuantityValue(ingredientElement.Quantity);
            if (newQuantity != null)
            {
                PrivateValueHelper.SetPrivateFieldValue<IDynamicValue>(ingredientElement, "<Quantity>k__BackingField", (IDynamicValue)newQuantity);
                ingredientElement.FirePropertyChanged("Quantity");
            }
            //TODO Нужно выводить ошибку если неудалось
        }

        public static MultiDynamicValue WrapDynamicQuantityValue(IDynamicValue origDynamicValue, float newThrottleK = 1)
        {
            if (CheckDynamicQuantityValueWrap(origDynamicValue))
            {
                UpdateDynamicQuantityValueWrap(origDynamicValue, newThrottleK);
                return (MultiDynamicValue) origDynamicValue;
            }
            else 
            {
                return new MultiDynamicValue(MultiDynamicOps.Multiply, new IDynamicValue[3]
                        {
                            origDynamicValue,
                            new ConstantValue(1f),
                            new ConstantValue(newThrottleK)
                        });
            }
        }

        public static IDynamicValue UnWrapDynamicQuantityValue(IDynamicValue origDynamicValue)
        {
            if (CheckDynamicQuantityValueWrap(origDynamicValue))
            {
                return (IDynamicValue) (origDynamicValue as MultiDynamicValue).Values[0];
            }
            return null;
        }

        public static bool CheckDynamicQuantityValueWrap(IDynamicValue origDynamicValue) 
        {
            if (origDynamicValue is MultiDynamicValue ) 
            {
                MultiDynamicValue multiDynamicValue = origDynamicValue as MultiDynamicValue;

                if (multiDynamicValue != null          
                    && multiDynamicValue.Values.Count == 3 
                    && multiDynamicValue.Values[1] is ConstantValue
                    && multiDynamicValue.Values[1].GetBaseValue == 1f
                    && multiDynamicValue.Values[2] is ConstantValue) 
                {
                    return true;
                }
            }
            return false;
        }

        public static void UpdateDynamicQuantityValueWrap(IDynamicValue origDynamicValue, float newThrottleK = 1)
        {
            MultiDynamicValue multiDynamicValue = origDynamicValue as MultiDynamicValue;

            PrivateValueHelper.SetPrivateFieldValue<float>(multiDynamicValue.Values[2], "<GetBaseValue>k__BackingField", newThrottleK);
            multiDynamicValue.Values[2].FirePropertyChanged("GetBaseValue");
        }

        public void UpdateRecipeFamilyGroup()
        {
            this.recipeFamilyGroup.Clear();
            Dictionary<Skill, int> skillGroup = new Dictionary<Skill, int>();
            Dictionary<Type, int> itemTypeGroup = new Dictionary<Type, int>();

            ConsoleLogWriter.Instance.Write("Start RecipeFamily Groups update " + "\n");

            // Заполнение группы у скилов
            foreach (Skill skill in Skill.AllSkills)
            {
                Item SkillScrollItem = Item.Get(SkillsLookUp.SkillToSkillScroll.GetOrDefault<Type, Type>(skill.GetType()));
                Item SkillBookItem = Item.Get(SkillsLookUp.SkillToSkillBook.GetOrDefault<Type, Type>(skill.GetType()));
                int calcGroup = skill.Tier;

                skillGroup.Add(skill, calcGroup);

                if (SkillScrollItem != null)
                {
                    itemTypeGroup.Add(SkillScrollItem.GetType(), calcGroup);
                    RecipeFamily.GetRecipesForItem(SkillScrollItem.GetType())
                        .ForEach(recipe => this.recipeFamilyGroup.Add(recipe, calcGroup));
                }

                if (SkillScrollItem != null)
                {
                    itemTypeGroup.Add(SkillBookItem.GetType(), calcGroup);
                    RecipeFamily.GetRecipesForItem(SkillBookItem.GetType())
                        .ForEach(recipe => this.recipeFamilyGroup.Add(recipe, calcGroup));
                }
            }

            //Заполнение группы 0 у items без рецептов
            foreach (Item item in Item.AllItems)
            {
                if (!itemTypeGroup.ContainsKey(item.GetType()) && RecipeFamily.GetRecipesForItem(item.GetType()).Count() == 0)
                    itemTypeGroup.Add(item.GetType(), 0);
            }

            //Циклы обхода по рецептам с попыткой посчитать группу
            for (int i = 1; i < 20 ; i++)
            // && this.recipeFamilyGroup.Count <= RecipeFamily.AllRecipes.Length
            {
                foreach (RecipeFamily recipeFamily in RecipeFamily.AllRecipes)
                {
                    if (this.recipeFamilyGroup.ContainsKey(recipeFamily)) continue;

                    int calcGroup = 0;
                    bool itemGroupSearchFail = false;
                    
                    foreach (IngredientElement ingredient in recipeFamily.Ingredients)
                    {
                        if (ingredient.Tag != null)
                        {
                            int tagItemGroup = -1;
                            foreach (Type type in TagManager.TagToTypes.GetOrDefault<Eco.Gameplay.Items.Tag, HashSet<Type>>(ingredient.Tag))
                            {
                                if (!itemTypeGroup.ContainsKey(type))
                                    itemGroupSearchFail = true;
                                else if (itemTypeGroup[type] < tagItemGroup || tagItemGroup < 0)
                                        tagItemGroup = itemTypeGroup[type];
                            }
                            if (tagItemGroup > calcGroup) calcGroup = tagItemGroup;
                        }
                        else if (ingredient.Item != null)
                        {    
                            if (!itemTypeGroup.ContainsKey(ingredient.Item.GetType()))
                                itemGroupSearchFail = true;
                            else if(itemTypeGroup[ingredient.Item.GetType()] > calcGroup)
                                    calcGroup = itemTypeGroup[ingredient.Item.GetType()];
                        }
                    }

                    foreach (RequiresSkillAttribute reqskill in recipeFamily.RequiredSkills)
                    {
                        if (!skillGroup.ContainsKey(reqskill.SkillItem))
                            itemGroupSearchFail = true;
                        else if(skillGroup[reqskill.SkillItem] > calcGroup)
                                calcGroup = skillGroup[reqskill.SkillItem];
                    }

                    if (!itemGroupSearchFail)
                    {
                        recipeFamily.Recipes
                            .SelectMany(recipe => recipe.Items)
                            .Where(product => !itemTypeGroup.ContainsKey(product.Item.GetType()))
                            .ToList()
                            .ForEach(product => itemTypeGroup.Add(product.Item.GetType(), calcGroup));
                        this.recipeFamilyGroup.Add(recipeFamily, calcGroup);
                    }
                }

            }
            ConsoleLogWriter.Instance.Write("groupsRecipeFamily.Count = " + recipeFamilyGroup.Count + "\n");
            ConsoleLogWriter.Instance.Write("groupsSkill.Count = " + skillGroup.Count + "\n");
            ConsoleLogWriter.Instance.Write("groupsItems.Count = " + itemTypeGroup.Count + "\n");
            ConsoleLogWriter.Instance.Write("RecipeFamily.AllRecipes.Length = " + RecipeFamily.AllRecipes.Length + "\n");
        }

        public float CalculateQuantityMultiplier(RecipeFamily recipeFamily)
        {
            float globalC = this.pluginConfig.Config.GlobalCoefficient;

            float result = 1f;

            if (recipeFamilyGroup.ContainsKey(recipeFamily))
            {
                switch (recipeFamilyGroup[recipeFamily])
                {
                    case 0:
                        result = globalC * this.pluginConfig.Config.Group0Multiplier;
                        break;
                    case 1:
                        result = globalC * this.pluginConfig.Config.Group1Multiplier;
                        break;
                    case 2:
                        result = globalC * this.pluginConfig.Config.Group2Multiplier;
                        break;
                    case 3:
                        result = globalC * this.pluginConfig.Config.Group3Multiplier;
                        break;
                    case 4:
                        result = globalC * this.pluginConfig.Config.Group4Multiplier;
                        break;
                    case 5:
                        result = globalC * this.pluginConfig.Config.Group5Multiplier;
                        break;
                    case 6:
                        result = globalC * this.pluginConfig.Config.Group6Multiplier;
                        break;
                    case 7:
                        result = globalC * this.pluginConfig.Config.Group7Multiplier;
                        break;
                    case 8:
                        result = globalC * this.pluginConfig.Config.Group8Multiplier;
                        break;
                }
            }
            else result = globalC;

            return result;
        }

    }

    [Localized]
    public class RecipeThrottleConfig : Singleton<RecipeThrottleConfig>
    {
        [LocCategory("Global")]
        [LocDescription("Enable RecipeThrottle wrap all Quantity values in every recipe in game to add aditional coefficient to it")]
        public bool Enable { get; set; } = false;

        [LocCategory("Global")]
        [LocDescription("The global coefficient affecting all recipes")]
        public float GlobalCoefficient { get; set; } = 1f;

        [LocCategory("Global")]
        [LocDescription("The global coefficient affecting the quantity. !! Will be implemented in the next release !! ")]
        public float impactsQuantity { get; set; } = 1f;

        [LocCategory("Global")]
        [LocDescription("The global coefficient affecting the crafting time. !! Will be implemented in the next release !! ")]
        public float impactsCraftTime { get; set; } = 1f;

        [LocCategory("Groups")]
        [LocDescription("Group 0 ")]
        public float Group0Multiplier { get; set; } = 1f;

        [LocCategory("Groups")]
        [LocDescription("Group 1 ")]
        public float Group1Multiplier { get; set; } = 1f;

        [LocCategory("Groups")]
        [LocDescription("Group 2 ")]
        public float Group2Multiplier { get; set; } = 1f;

        [LocCategory("Groups")]
        [LocDescription("Group 3 ")]
        public float Group3Multiplier { get; set; } = 1f;

        [LocCategory("Groups")]
        [LocDescription("Group 4 ")]
        public float Group4Multiplier { get; set; } = 1f;

        [LocCategory("Groups")]
        [LocDescription("Group 5 ")]
        public float Group5Multiplier { get; set; } = 1f;

        [LocCategory("Groups")]
        [LocDescription("Group 6 ")]
        public float Group6Multiplier { get; set; } = 1f;

        [LocCategory("Groups")]
        [LocDescription("Group 7 ")]
        public float Group7Multiplier { get; set; } = 1f;

        [LocCategory("Groups")]
        [LocDescription("Group 8 ")]
        public float Group8Multiplier { get; set; } = 1f;

    }
    public class RecipeThrottlePlugin : 
        IModKitPlugin,
        IDisplayablePlugin,
        IInitializablePlugin, 
        IShutdownablePlugin, 
        IConfigurablePlugin,
        IHasDisplayTabs,
        IGUIPlugin,
        IDisplayTab
    {
        PluginConfig<RecipeThrottleConfig> config;
        public RecipeThrottle recipeThrottle = new RecipeThrottle();
        public IPluginConfig PluginConfig => this.config;
        public ThreadSafeAction<object, string> ParamChanged { get; set; } = new();

        string status = string.Empty;
        public string GetStatus() => this.status;
        public string GetCategory() => "Mods";
        public override string ToString() => "RecipeThrottle";

        public void Initialize(TimedTask timer)
        {
            this.status = "Ready.";
            this.config = new PluginConfig<RecipeThrottleConfig>("RecipeThrottleConfig");  // Load our plugin configuration
            recipeThrottle.Initialize(this.config);
            
            //UserManager.OnUserLoggedIn.Add(this.OnUserLogin);                   // Register our OnUserLoggedIn event handler for showing players our welcome message.
        }
        public Task ShutdownAsync()
        {
            //UserManager.OnUserLoggedIn.Remove(this.OnUserLogin);                // Remove our OnUserLoggedIn event handler
            return Task.CompletedTask;
        }

        string GetDisplayTitle() => "RecipeFamily Grops";
        public string GetDisplayText()
        {
            StringBuilder stringBuilder = new StringBuilder(1024);
            stringBuilder.AppendLine((string)Localizer.DoStr("RecipeFamily Group:"));
            foreach (var line in this.recipeThrottle.recipeFamilyGroup)
                stringBuilder.AppendLine($"{line.Value} | {line.Key.DisplayName.ToString()}");
            stringBuilder.AppendLine();

            return stringBuilder.ToString();
        }
        public object GetEditObject() => this.config.Config;
        public void OnEditObjectChanged(object o, string param)
        {
            if (param == "Enable")
                OnParamEnableChanged();
            else if (this.config.Config.Enable)
            {
                OnGlobalCoefficientChanged();
            }
                 
        }
        private void OnGlobalCoefficientChanged()
        {
            if (this.config.Config.Enable)
            {
                foreach (RecipeFamily recipeFamily in (RecipeFamily.AllRecipes))
                {
                    foreach (Recipe recept in recipeFamily.Recipes)
                    {
                        foreach (IngredientElement ingredientElement in recept.Ingredients)
                        {
                            RecipeThrottle.UpdateDynamicQuantityValueWrap(ingredientElement.Quantity, recipeThrottle.CalculateQuantityMultiplier(recipeFamily));
                        }
                        recept.FirePropertyChanged("Ingredients");
                    }
                    recipeFamily.FirePropertyChanged("Recipes");
                    recipeFamily.FirePropertyChanged("Ingredients");
                }
            }
        }

        private void OnParamEnableChanged()
        {
            if (this.config.Config.Enable)
            {
                int i = 0;

                ConsoleLogWriter.Instance.Write("RecipeThrottle: Try to inject Quantity value for " + RecipeFamily.AllRecipes.Length + " RecipeFamilys.\n");
                foreach (RecipeFamily recipeFamily in (RecipeFamily.AllRecipes))
                {
                    foreach (Recipe recept in recipeFamily.Recipes)
                    {
                        foreach (IngredientElement ingredientElement in recept.Ingredients)
                        {
                            RecipeThrottle.InsertRecipeThrottleK(ingredientElement, recipeThrottle.CalculateQuantityMultiplier(recipeFamily));
                            i++;
                        }
                        recept.FirePropertyChanged("Ingredients");
                    }
                    recipeFamily.FirePropertyChanged("Recipes");
                    recipeFamily.FirePropertyChanged("Ingredients");
                    //DELME
                    string str = "";
                    str += ("RecipeFamily: " + recipeFamily.DisplayName + " RequiredSkills: "); 
                        foreach ( RequiresSkillAttribute skill in recipeFamily.RequiredSkills )
                        {
                        str += (skill.SkillItem.DisplayName + "; ");
                        }
                        foreach ( IngredientElement ingredient in recipeFamily.DefaultRecipe.Ingredients )
                        {
                        str += (ingredient.InnerName + "; ");
                        }
                    str += "\n";
                    //ConsoleLogWriter.Instance.Write(str);
                    //DELME

                }
                ConsoleLogWriter.Instance.Write("RecipeThrottle: Success inject Quantity value for " + i + " IngredientElements.\n");
                this.status = "Enabled.";
            }
            else
            {
                int i = 0;
                ConsoleLogWriter.Instance.Write("RecipeThrottle: Try to remove RecipeThrottle for " + RecipeFamily.AllRecipes.Length + " RecipeFamilys.\n");
                foreach (RecipeFamily recipeFamily in (RecipeFamily.AllRecipes))
                {
                    foreach (Recipe recept in recipeFamily.Recipes)
                    {
                        foreach (IngredientElement ingredientElement in recept.Ingredients)
                        {
                            RecipeThrottle.RemoveRecipeThrottleK(ingredientElement);
                            i++;
                        }
                        recept.FirePropertyChanged("Ingredients");
                    }
                    recipeFamily.FirePropertyChanged("Recipes");
                    recipeFamily.FirePropertyChanged("Ingredients");
                }
                ConsoleLogWriter.Instance.Write("RecipeThrottle: Success remove wraps for " + i + " IngredientElements.\n");
                this.status = "Disabled.";
            }
        }



    }
}

#pragma warning restore CA1416 // Проверка совместимости платформы