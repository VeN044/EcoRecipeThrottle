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
                CreateAndPrintGroups();
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

        private Dictionary<RecipeFamily, int> groupsRecipeFamily = new Dictionary<RecipeFamily, int>();
        private Dictionary<Skill, int> groupsSkill = new Dictionary<Skill, int>();
        private Dictionary<Type, int> groupsItems = new Dictionary<Type, int>();
        void CreateAndPrintGroups()
        {
            groupsRecipeFamily.Clear();
            groupsSkill.Clear();
            groupsItems.Clear();

            // Заполнение группы у скилов
            foreach (Skill skill in Skill.AllSkills)
            {
                
                Item SkillScrollItem = Item.Get(SkillsLookUp.SkillToSkillScroll.GetOrDefault<Type, Type>(skill.GetType()));
                Item SkillBookItem = Item.Get(SkillsLookUp.SkillToSkillBook.GetOrDefault<Type, Type>(skill.GetType()));
                int tGroup = skill.Tier;
                groupsSkill.Add(skill, tGroup);

                if (SkillScrollItem != null)
                {
                    groupsItems.Add(SkillScrollItem.GetType(), tGroup);
                    IEnumerable<RecipeFamily> ScrollrecipeFamilies = RecipeFamily.GetRecipesForItem(SkillScrollItem.GetType());
                    foreach (RecipeFamily recipe in ScrollrecipeFamilies)
                    {
                        groupsRecipeFamily.Add(recipe, tGroup);
                    }
                }

                if(SkillScrollItem != null)
                {
                    groupsItems.Add(SkillBookItem.GetType(), tGroup);
                    IEnumerable<RecipeFamily> BookrecipeFamilies = RecipeFamily.GetRecipesForItem(SkillBookItem.GetType());
                    foreach (RecipeFamily recipe in BookrecipeFamilies)
                    {
                        groupsRecipeFamily.Add(recipe, tGroup);
                    }
                }
            }

            //Заполнение группы 0 у items без рецептов
            foreach (Item item in Item.AllItems)
            {
                if (groupsItems.ContainsKey(item.GetType())) continue;
                if (RecipeFamily.GetRecipesForItem(item.GetType()).Count() == 0)
                {
                    groupsItems.Add(item.GetType(), 0);
                }
            }


            foreach (var i in groupsSkill)
            {
                ConsoleLogWriter.Instance.Write("groupsSkill " + (i.Value.ToString() + " | " + i.Key.DisplayName) + "\n");
            }


            //Циклы обхода по рецептам с попыткой посчитать группу
            for (int i = 1;i<20;i++ ) 
            {
                ConsoleLogWriter.Instance.Write("groupsRecipeFamily.Count = " + groupsRecipeFamily.Count + "\n");

                ConsoleLogWriter.Instance.Write("groupsSkill.Count = " + groupsSkill.Count + "\n");
                ConsoleLogWriter.Instance.Write("groupsItems.Count = " + groupsItems.Count + "\n");
                ConsoleLogWriter.Instance.Write("RecipeFamily.AllRecipes.Length = " + RecipeFamily.AllRecipes.Length + "\n");
                ConsoleLogWriter.Instance.Write(" Main recipe cycle round " + i + "\n");
                foreach (RecipeFamily recipeFamily in (RecipeFamily.AllRecipes))
                {
                    if (groupsRecipeFamily.ContainsKey(recipeFamily)) continue;
                    ConsoleLogWriter.Instance.Write("Try to calc Group for RecipeFamily = " + recipeFamily.DisplayName.ToString() + "\n");
                    // Получить неободимые skill для рецепта
                    RequiresSkillAttribute[] skills = recipeFamily.RequiredSkills;
                    // Получить ингредиенты для рецепта
                    IngredientElement[] Ingredients = recipeFamily.Ingredients;
                    // Получить продкцию для рецепта
                    CraftingElement[] Products = recipeFamily.Product;

                    // Вычислить группу
                    int t = 0;
                    bool fail = false;
                    foreach (IngredientElement ingredient in Ingredients)
                    {
                        Item item = null;
                        if (ingredient.Tag != null)
                        {
                            int internalT = -1;
                            foreach (Type type in TagManager.TagToTypes.GetOrDefault<Eco.Gameplay.Items.Tag, HashSet<Type>>(ingredient.Tag))
                            {
                                if (!groupsItems.ContainsKey(type))
                                {
                                    ConsoleLogWriter.Instance.Write(" Fail search item in tag " + type.ToString() + "\n");
                                    fail = true;
                                }
                                else
                                {
                                    if (groupsItems[type] < internalT || internalT < 0) internalT = groupsItems[type];
                                }
                            }
                            if (internalT > t) t = internalT;
                        }
                        else if (ingredient.Item != null)
                        {
                            if (!groupsItems.ContainsKey(ingredient.Item.GetType()))
                            {
                                ConsoleLogWriter.Instance.Write(" Fail search item as item " + ingredient.Item.ToString() + "\n");
                                fail = true;
                            }
                            else
                            {
                                if (groupsItems[ingredient.Item.GetType()] > t) t = groupsItems[ingredient.Item.GetType()];
                            }
                        }
                        else ConsoleLogWriter.Instance.Write(" Erorr in tag !!!\n");
                    }

                    foreach (RequiresSkillAttribute reqskill in skills)
                    {
                        if (!groupsSkill.ContainsKey(reqskill.SkillItem))
                        {
                            ConsoleLogWriter.Instance.Write(" Fail search skill " + reqskill.SkillItem.ToString() + "\n");
                            fail = true;
                        }
                        else if (groupsSkill[reqskill.SkillItem] > t)
                        {
                            //ConsoleLogWriter.Instance.Write(" Skill " + reqskill.SkillItem.ToString() + " set t to " + groupsSkill[reqskill.SkillItem] + "\n");
                            t = groupsSkill[reqskill.SkillItem];
                        }
                    }

                    if (!fail)
                    {
                        // записать группу для product
                        foreach (Recipe recipe in recipeFamily.Recipes)
                        {
                            foreach (CraftingElement product in recipe.Items)
                            {
                                if (!groupsItems.ContainsKey(product.Item.GetType()))
                                {
                                    ConsoleLogWriter.Instance.Write("Add to groupsItems new Item " + product.Item.GetType().ToString() + "| " + t + "\n");
                                    groupsItems.Add(product.Item.GetType(), t);
                                }

                            }
                        }

                        // записать группу для RecipeFamily
                        groupsRecipeFamily.Add(recipeFamily, t);
                        ConsoleLogWriter.Instance.Write("Add recipeFamily " + recipeFamily.DisplayName.ToString() + "| " + t + "\n");
                    }
                }
            }


            foreach (var i in groupsRecipeFamily)
            {
                ConsoleLogWriter.Instance.Write( "TGroup: " + (i.Value.ToString() + " | RecipeFamily: " + i.Key.DisplayName) + "\n"  );
            }
            

        }
        int GetRecipeGroup(RecipeFamily recipeFamily) 
        {
            CraftingElement[] products = recipeFamily.Product;

            IngredientElement[] ingredients = recipeFamily.Ingredients ;
            
            return 0;


        }
    }
}

#pragma warning restore CA1416 // Проверка совместимости платформы