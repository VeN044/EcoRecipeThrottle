using Eco.Core.Plugins;
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

#pragma warning disable CA1416 // Проверка совместимости платформы

namespace Eco.Mod.VeN.RecipeThrottle
{
    internal static class RecipeThrottle
    {
        /// Manipulete Wraping of IDynamicValue to insert Multiply koefficient to it.
   
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

    }

    [Localized]
    public class RecipeThrottleConfig : Singleton<RecipeThrottleConfig>
    {
        [LocDescription("Enable RecipeThrottle wrap all Quantity values in every recipe in game to add aditional coefficient to it")]
        public bool RecipeThrottleEnable { get; set; } = false;

        [LocDescription("The global coefficient affecting all recipes")]
        public float RecipeThrottleGlobalKValue { get; set; } = 1f;
    }
    public class RecipeThrottlePlugin : IModKitPlugin, IInitializablePlugin, IShutdownablePlugin, IConfigurablePlugin
    {
        PluginConfig<RecipeThrottleConfig> config;
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
            OnRecipeThrottleEnableChanged();
            //UserManager.OnUserLoggedIn.Add(this.OnUserLogin);                   // Register our OnUserLoggedIn event handler for showing players our welcome message.
        }
        public Task ShutdownAsync()
        {
            //UserManager.OnUserLoggedIn.Remove(this.OnUserLogin);                // Remove our OnUserLoggedIn event handler
            return Task.CompletedTask;
        }
        public object GetEditObject() => this.config.Config;
        public void OnEditObjectChanged(object o, string param)
        {
            if (param == "RecipeThrottleEnable")
                OnRecipeThrottleEnableChanged();
            else if (param == "RecipeThrottleGlobalKValue" && this.config.Config.RecipeThrottleEnable)
            {
                OnRecipeThrottleGlobalStaticValueChanged();
            }
        }
        private void OnRecipeThrottleGlobalStaticValueChanged()
        {
            float k = this.config.Config.RecipeThrottleGlobalKValue;
            if (this.config.Config.RecipeThrottleEnable)
            {
                foreach (RecipeFamily recipeFamily in (RecipeFamily.AllRecipes))
                {
                    foreach (Recipe recept in recipeFamily.Recipes)
                    {
                        foreach (IngredientElement ingredientElement in recept.Ingredients)
                        {
                            RecipeThrottle.UpdateDynamicQuantityValueWrap(ingredientElement.Quantity, k);
                        }
                        recept.FirePropertyChanged("Ingredients");
                    }
                    recipeFamily.FirePropertyChanged("Recipes");
                    recipeFamily.FirePropertyChanged("Ingredients");
                }
            }
        }

        private void OnRecipeThrottleEnableChanged()
        {
            if (this.config.Config.RecipeThrottleEnable)
            {
                int i = 0;
                float k = this.config.Config.RecipeThrottleGlobalKValue;
                ConsoleLogWriter.Instance.Write("RecipeThrottle: Try to inject Quantity value for " + RecipeFamily.AllRecipes.Length + " RecipeFamilys.\n");
                foreach (RecipeFamily recipeFamily in (RecipeFamily.AllRecipes))
                {
                    foreach (Recipe recept in recipeFamily.Recipes)
                    {
                        foreach (IngredientElement ingredientElement in recept.Ingredients)
                        {
                            RecipeThrottle.InsertRecipeThrottleK(ingredientElement, k);
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
                UpdateRecipeFamilyGroup();
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

        private Dictionary<RecipeFamily, int> recipeFamilyGroup = new Dictionary<RecipeFamily, int>();

        void UpdateRecipeFamilyGroup()
        {
            recipeFamilyGroup.Clear();
            Dictionary<Skill, int> skillGroup = new Dictionary<Skill, int>();
            Dictionary<Type, int> itemTypeGroup = new Dictionary<Type, int>();

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
                        .ForEach(recipe => recipeFamilyGroup.Add(recipe, calcGroup));
                }

                if(SkillScrollItem != null)
                {
                    itemTypeGroup.Add(SkillBookItem.GetType(), calcGroup);
                    RecipeFamily.GetRecipesForItem(SkillBookItem.GetType())
                        .ForEach(recipe => recipeFamilyGroup.Add(recipe, calcGroup));

                }
            }

            //Заполнение группы 0 у items без рецептов
            foreach (Item item in Item.AllItems)
            {
                if (!itemTypeGroup.ContainsKey(item.GetType()) && RecipeFamily.GetRecipesForItem(item.GetType()).Count() == 0) 
                    itemTypeGroup.Add(item.GetType(), 0);
            }

            //Циклы обхода по рецептам с попыткой посчитать группу
            for (int i = 1;i<20;i++ ) 
            {
                foreach (RecipeFamily recipeFamily in RecipeFamily.AllRecipes)
                {
                    if (recipeFamilyGroup.ContainsKey(recipeFamily)) continue;

                    int calcGroup = 0;
                    bool itemGroupSearchFail = false;

                    foreach (IngredientElement ingredient in recipeFamily.Ingredients)
                    {
                        if (ingredient.Tag != null)
                        {
                            int tagItemGroup = -1;
                            foreach (Type type in TagManager.TagToTypes.GetOrDefault<Eco.Gameplay.Items.Tag, HashSet<Type>>(ingredient.Tag))
                            {
                                if (itemTypeGroup.ContainsKey(type) && (itemTypeGroup[type] < tagItemGroup || tagItemGroup < 0))
                                    tagItemGroup = itemTypeGroup[type];
                                else itemGroupSearchFail = true;
                            }
                            if (tagItemGroup > calcGroup) calcGroup = tagItemGroup;
                        }
                        else if (ingredient.Item != null)
                        {
                            if (itemTypeGroup.ContainsKey(ingredient.Item.GetType()) && itemTypeGroup[ingredient.Item.GetType()] > calcGroup)
                                calcGroup = itemTypeGroup[ingredient.Item.GetType()];
                            else itemGroupSearchFail = true;
                        }
                    }

                    foreach (RequiresSkillAttribute reqskill in recipeFamily.RequiredSkills)
                    {
                        if (skillGroup.ContainsKey(reqskill.SkillItem) && skillGroup[reqskill.SkillItem] > calcGroup)
                            calcGroup = skillGroup[reqskill.SkillItem];
                        else itemGroupSearchFail = true;
                    }

                    if (!itemGroupSearchFail)
                    {
                        recipeFamily.Recipes
                            .SelectMany(recipe => recipe.Items)
                            .Where(product => !itemTypeGroup.ContainsKey(product.Item.GetType()))
                            .ToList()
                            .ForEach(product => itemTypeGroup.Add(product.Item.GetType(), calcGroup));
                        recipeFamilyGroup.Add(recipeFamily, calcGroup);
                    }
                }
                if (RecipeFamily.AllRecipes.Length <= recipeFamilyGroup.Count) break;
            }

            ConsoleLogWriter.Instance.Write("groupsRecipeFamily.Count = " + recipeFamilyGroup.Count + "\n");
            ConsoleLogWriter.Instance.Write("groupsSkill.Count = " + skillGroup.Count + "\n");
            ConsoleLogWriter.Instance.Write("groupsItems.Count = " + itemTypeGroup.Count + "\n");
            ConsoleLogWriter.Instance.Write("RecipeFamily.AllRecipes.Length = " + RecipeFamily.AllRecipes.Length + "\n");

        }
    }
}

#pragma warning restore CA1416 // Проверка совместимости платформы