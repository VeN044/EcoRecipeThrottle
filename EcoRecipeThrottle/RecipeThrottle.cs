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
using Eco.Core.Items;
using Eco.Simulation.Agents.AI;
using Eco.ModKit;
using System.Collections;
using Eco.Mods.TechTree;
using Eco.Mod.VeN.RecipeThrottle;
using System.Timers;
using System.Threading;
using System.Threading.Tasks;
using Eco.Simulation.WorldLayers;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Eco.Simulation.Time;

using Eco.Shared.Serialization;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Eco.Gameplay.Components;
using Eco.Gameplay.Objects;
using Eco.Shared.IoC;



#pragma warning disable CA1416 // Проверка совместимости платформы

namespace Eco.Mod.VeN.RecipeThrottle
{
    public enum TrottleImpact
    {
        [LocDisplayName("  0% no changes")]
        None,
        [LocDisplayName(" 25% low changes")]
        Low,
        [LocDisplayName(" 50% mid changes")]
        Medium,
        [LocDisplayName(" 75% hight changes")]
        Hight,
        [LocDisplayName("100% full impact")]
        Full
    }

    public static class TrottleImpactHelpers
    {
        private static Dictionary<TrottleImpact, float> impactPercentages = new Dictionary<TrottleImpact, float>
    {
        { TrottleImpact.None, 0f },
        { TrottleImpact.Low, 0.25f },
        { TrottleImpact.Medium, 0.50f },
        { TrottleImpact.Hight, 0.75f },
        { TrottleImpact.Full, 1f }
    };

        public static float GetPercentage(TrottleImpact impact)
        {
            if (impactPercentages.ContainsKey(impact))
            {
                return impactPercentages[impact];
            }
            return 0f; 
        }
    }


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


        public static void PatchIngredientElementValues (IngredientElement ingredientElement , float newThrottleK = 1)
        {
            MultiDynamicValue newQuantity = WrapDynamicValue(ingredientElement.Quantity, newThrottleK);
            PrivateValueHelper.SetPrivateFieldValue<IDynamicValue>(ingredientElement, "<Quantity>k__BackingField", (IDynamicValue)newQuantity );
            ingredientElement.FirePropertyChanged("Quantity");
            //TODO Нужно выводить ошибку если неудалось
        }

        public static void PatchRecipeFamilyValues (RecipeFamily recipeFamily, float calloriesMultiplyer, float craftMinutesMultiplyer)
        {
            MultiDynamicValue newLaborInCalories = WrapDynamicValue(recipeFamily.LaborInCalories , calloriesMultiplyer);
            recipeFamily.LaborInCalories = newLaborInCalories;
            recipeFamily.FirePropertyChanged("LaborInCallories");
            //PrivateValueHelper.SetPrivateFieldValue<float>(recipeFamily, "<Labor>k__BackingField", recipeFamily.LaborInCalories.GetBaseValue);

            recipeFamily.FirePropertyChanged("Labor");


            MultiDynamicValue newCraftMinutes = WrapDynamicValue(recipeFamily.CraftMinutes, craftMinutesMultiplyer);
            PrivateValueHelper.SetPrivateFieldValue<IDynamicValue>(recipeFamily, "<CraftMinutes>k__BackingField", (IDynamicValue)newCraftMinutes);
            recipeFamily.FirePropertyChanged("CraftMinutes");
        }

        public static void UnPatchIngredientElementValues (IngredientElement ingredientElement )
        {
            IDynamicValue newQuantity = UnWrapDynamicValue(ingredientElement.Quantity);
            if (newQuantity != null)
            {
                PrivateValueHelper.SetPrivateFieldValue<IDynamicValue>(ingredientElement, "<Quantity>k__BackingField", (IDynamicValue)newQuantity);
                ingredientElement.FirePropertyChanged("Quantity");
            }
            //TODO Нужно выводить ошибку если неудалось
        }

        public static void UnPatchRecipeFamilyValues (RecipeFamily recipeFamily)
        {
            IDynamicValue newLaborInCalories = UnWrapDynamicValue(recipeFamily.LaborInCalories);
            if (newLaborInCalories != null)
            {
                recipeFamily.LaborInCalories = newLaborInCalories;
                recipeFamily.FirePropertyChanged("LaborInCallories");         
                //PrivateValueHelper.SetPrivateFieldValue<float>(recipeFamily, "<Labor>k__BackingField", recipeFamily.LaborInCalories.GetBaseValue);
                recipeFamily.FirePropertyChanged("Labor");
            }

            IDynamicValue newCraftMinutes = UnWrapDynamicValue(recipeFamily.CraftMinutes);
            if (newCraftMinutes != null)
            {
                PrivateValueHelper.SetPrivateFieldValue<IDynamicValue>(recipeFamily, "<CraftMinutes>k__BackingField", (IDynamicValue)newCraftMinutes);
                recipeFamily.FirePropertyChanged("CraftMinutes");
            }
        }

        public static MultiDynamicValue WrapDynamicValue(IDynamicValue origDynamicValue, float newThrottleK = 1)
        {
            if (IsRecipeThrottleWrap(origDynamicValue))
            {
                UpdateWrapValue(origDynamicValue, newThrottleK);
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

        public static IDynamicValue UnWrapDynamicValue(IDynamicValue origDynamicValue)
        {
            if (IsRecipeThrottleWrap(origDynamicValue))
            {
                return (IDynamicValue) (origDynamicValue as MultiDynamicValue).Values[0];
            }
            return null;
        }

        public static bool IsRecipeThrottleWrap(IDynamicValue origDynamicValue) 
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

        public static void UpdateWrapValue(IDynamicValue origDynamicValue, float newThrottleK = 1)
        {
            MultiDynamicValue multiDynamicValue = origDynamicValue as MultiDynamicValue;

            //multiDynamicValue.Values[2] = new ConstantValue(newThrottleK);
            PrivateValueHelper.SetPrivateFieldValue<float>(multiDynamicValue.Values[2], "<GetBaseValue>k__BackingField", newThrottleK);
            multiDynamicValue.Values[2].FirePropertyChanged("GetBaseValue");
            multiDynamicValue.FirePropertyChanged("Values");
        }

        public void UpdateRecipeFamilyGroup()
        {
            this.recipeFamilyGroup.Clear();
            Dictionary<Skill, int> skillGroup = new Dictionary<Skill, int>();
            Dictionary<Type, int> itemTypeGroup = new Dictionary<Type, int>();
            int maxGroup = 0;

            //Заполнение группы 0 у items без рецептов
            foreach (Item item in Item.AllItems)
            {
                if (!itemTypeGroup.ContainsKey(item.GetType()) && RecipeFamily.GetRecipesForItem(item.GetType()).Count() == 0)
                    itemTypeGroup.Add(item.GetType(), 0);
            }

            //Заполнение группы 0 у skill без книжек или листков
            foreach (Skill skill in Skill.AllSkills)
            {
                if ( RecipeFamily.GetRecipesForItem(SkillsLookUp.SkillToSkillBook.GetOrDefault<Type, Type>(skill.GetType())).Count<RecipeFamily>() == 0 )
                    skillGroup.Add(skill, 1);
            }

            //Циклы обхода по рецептам с попыткой посчитать группу
            bool deadLoopDetected = false;
            for (int i = 1; i < 30 && this.recipeFamilyGroup.Count <= RecipeFamily.AllRecipes.Length; i++)
            {
                int changesCount = 0;

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
                            int goodItemsInTagCount = 0;
                            bool saveGroupSearchFail = itemGroupSearchFail;

                            foreach (Type type in TagManager.TagToTypes.GetOrDefault<Eco.Gameplay.Items.Tag, HashSet<Type>>(ingredient.Tag))
                            {
                                if (!itemTypeGroup.ContainsKey(type) )
                                {
                                    itemGroupSearchFail = true;
                                }
                                else if (itemTypeGroup[type] < tagItemGroup || tagItemGroup < 0)
                                {
                                    tagItemGroup = itemTypeGroup[type];
                                    goodItemsInTagCount++;
                                }
                                    
                            }
                            if (tagItemGroup > calcGroup) calcGroup = tagItemGroup;
                            if (deadLoopDetected && goodItemsInTagCount > 0) itemGroupSearchFail = saveGroupSearchFail;
                        }
                        else if (ingredient.Item != null)
                        {
                            if (!itemTypeGroup.ContainsKey(ingredient.Item.GetType()))
                            { 
                                itemGroupSearchFail = true;
                            }
                            else if (itemTypeGroup[ingredient.Item.GetType()] > calcGroup)
                            calcGroup = itemTypeGroup[ingredient.Item.GetType()];
                        }
                    }

                    foreach (RequiresSkillAttribute reqskill in recipeFamily.RequiredSkills)
                    {
                        if (!skillGroup.ContainsKey(reqskill.SkillItem))
                        {
                            itemGroupSearchFail = true;
                        }
                        else if (skillGroup[reqskill.SkillItem] > calcGroup)
                            calcGroup = skillGroup[reqskill.SkillItem];
                    }

                    if (!itemGroupSearchFail)
                    {
                        foreach (Recipe recipe in recipeFamily.Recipes)
                        {
                            foreach (CraftingElement product in recipe.Items)
                            {
                                if (!itemTypeGroup.ContainsKey(product.Item.GetType()))
                                {
                                    if (product.Item is SkillBook)
                                    {
                                        calcGroup += 1 ;
                                        Skill skillByBook = (Skill)Item.Get(SkillsLookUp.SkillToSkillBook.FirstOrDefault(x => x.Value == product.Item.GetType()).Key);
                                        if (skillByBook != null && !skillGroup.ContainsKey(skillByBook))
                                            skillGroup.Add(skillByBook, calcGroup);

                                    }
                                    else if (product.Item is SkillScroll)
                                    {
                                        calcGroup += 1;
                                        Skill skillByScroll = (Skill)Item.Get(SkillsLookUp.SkillToSkillScroll.FirstOrDefault(x => x.Value == product.Item.GetType()).Key);
                                        if (skillByScroll != null && !skillGroup.ContainsKey(skillByScroll))
                                            skillGroup.Add(skillByScroll, calcGroup);
                                    }
                                    itemTypeGroup.Add(product.Item.GetType(), calcGroup);
                                    
                                }
                            }
                        }
                        
                        recipeFamily.Recipes
                            .SelectMany(recipe => recipe.Items)
                            .Where(product => !itemTypeGroup.ContainsKey(product.Item.GetType()))
                            .ToList()
                            .ForEach(product => itemTypeGroup.Add(product.Item.GetType(), calcGroup));

                        this.recipeFamilyGroup.Add(recipeFamily, calcGroup);
                        maxGroup = maxGroup < calcGroup ? calcGroup : maxGroup;
                        changesCount++;
                    }
                }

                if (changesCount > 0) deadLoopDetected = false;
                else deadLoopDetected = true;

            }

            ConsoleLogWriter.Instance.Write("groupsRecipeFamily.Count = " + recipeFamilyGroup.Count + "\n");
            ConsoleLogWriter.Instance.Write("groupsSkill.Count = " + skillGroup.Count + "\n");
            ConsoleLogWriter.Instance.Write("groupsItems.Count = " + itemTypeGroup.Count + "\n");
            ConsoleLogWriter.Instance.Write("RecipeFamily.AllRecipes.Length = " + RecipeFamily.AllRecipes.Length + "\n");

            ConsoleLogWriter.Instance.Write("maxGroup = " + maxGroup + "\n");
            pluginConfig.Config.UpdateRecipeGroupsSettingsCollection(maxGroup+1);

        }

        public float CalculateMultiplier(RecipeFamily recipeFamily , MultiplierType multiplierType)
        {
            float globalQuantityMultiplier = this.pluginConfig.Config.GlobalQuantityMultiplier;
            float globalCraftTimeMultiplier = this.pluginConfig.Config.GlobalCraftTimeMultiplier;
            float globalCaloriesMultiplier = this.pluginConfig.Config.GlobalCaloriesMultiplier;

            RecipeThrottleConfig config = this.pluginConfig.Config;

            float result = 1f;       

            switch (multiplierType)
            {
                case MultiplierType.Quantity :
                    if (recipeFamilyGroup.ContainsKey(recipeFamily))
                    {
                        float timeParametr = GetResultOfTimeFunction(config.RecipeGroupsSettings[recipeFamilyGroup[recipeFamily]].StartFallTime, config.RecipeGroupsSettings[recipeFamilyGroup[recipeFamily]].EndFallTime);
                        result *= (config.RecipeGroupsSettings[recipeFamilyGroup[recipeFamily]].QuantityMultiplier - 1) * timeParametr + 1;
                    }                        
                    result *= globalQuantityMultiplier;
                    break;
                case MultiplierType.CraftTime :
                    if (recipeFamilyGroup.ContainsKey(recipeFamily))
                    {
                        float timeParametr = GetResultOfTimeFunction(config.RecipeGroupsSettings[recipeFamilyGroup[recipeFamily]].StartFallTime, config.RecipeGroupsSettings[recipeFamilyGroup[recipeFamily]].EndFallTime);
                        result *= (config.RecipeGroupsSettings[recipeFamilyGroup[recipeFamily]].CraftTimeMultiplier - 1) * timeParametr + 1;
                    }
                     
                    result = result * globalCraftTimeMultiplier;
                    break;
                case MultiplierType.Callories :
                    if (recipeFamilyGroup.ContainsKey(recipeFamily))
                    {
                        float timeParametr = GetResultOfTimeFunction(config.RecipeGroupsSettings[recipeFamilyGroup[recipeFamily]].StartFallTime, config.RecipeGroupsSettings[recipeFamilyGroup[recipeFamily]].EndFallTime);
                        result *= (config.RecipeGroupsSettings[recipeFamilyGroup[recipeFamily]].CaloriesMultiplier - 1) * timeParametr + 1;
                    }
                    result = result * globalCaloriesMultiplier;
                    break;        
            }
            return result;
        }

        public float GetResultOfTimeFunction(int startTime = 0, int endTime = 0, int pointTime = 0)
        {
            if (pointTime == 0) pointTime = (int)WorldTime.Seconds;

            if (startTime > pointTime) return 1f;
            else if (endTime < pointTime) return 0f;
            else
            {
                return 1f / (endTime - startTime) * (endTime - pointTime);
            }
        }
    }




    public enum MultiplierType
    {
        Quantity,
        CraftTime,
        Callories
    }

    public enum FallFunctionType
    {
        Linear,
        Sinusoidal
    }

    [Localized(true, false, "", false)]
    [TypeConverter(typeof(System.ComponentModel.ExpandableObjectConverter))]
    public class RecipeGroupSettings
    {
        [Browsable(false)]
        public int Id { get; set; } = 0;

        [LocCategory("Parametrs")]
        public float QuantityMultiplier { get; set; } = 1f;

        [LocCategory("Parametrs")]
        public float CraftTimeMultiplier { get; set; } = 1f;

        [LocCategory("Parametrs")]
        public float CaloriesMultiplier { get; set; } = 1f;

        [LocCategory("Main timer")]
        public int StartFallTime { get; set; } = 0;
        [LocCategory("Optional timers")]
        public int FirstQuarterEndFallTime { get; set; } = 0;
        [LocCategory("Optional timers")]
        public int SecondQuarterEndFallTime { get; set; } = 0;
        [LocCategory("Optional timers")]
        public int ThirdQuarterEndFallTime { get; set; } = 0;
        [LocCategory("Main timer")]
        public int EndFallTime { get; set; } = 0;
        public FallFunctionType FunctionType { get; set; } = FallFunctionType.Linear;
        public override string ToString()
        {
            DefaultInterpolatedStringHandler interpolatedStringHandler = new DefaultInterpolatedStringHandler(3, 2);
            interpolatedStringHandler.AppendFormatted("RecipeFamily Group");
            interpolatedStringHandler.AppendLiteral(" - ");
            interpolatedStringHandler.AppendFormatted<int>(this.Id);
            return interpolatedStringHandler.ToStringAndClear();
        }
    }


    [Localized(true, false, "", false)]
    [TypeConverter(typeof(System.ComponentModel.ExpandableObjectConverter))]
    public class RecipeThrottleConfig : Singleton<RecipeThrottleConfig>
    {
        [LocCategory("Global")]
        [LocDescription("Enable RecipeThrottle wrap all Quantity values in every recipe in game to add aditional coefficient to it")]
        public bool Enable { get; set; } = false;

        [LocCategory("Global")]
        [LocDescription("The global Quantity coefficient affecting all recipes")]
        public float GlobalQuantityMultiplier { get; set; } = 1f;

        [LocCategory("Global")]
        [LocDescription("")]
        public float GlobalCraftTimeMultiplier { get; set; } = 1f;

        [LocCategory("Global")]
        [LocDescription("")]
        public float GlobalCaloriesMultiplier { get; set; } = 1f;

/*        [LocCategory("Global")]
        public TrottleImpact QuantityImpact { get; set; } = TrottleImpact.Full;

        [LocCategory("Global")]
        public TrottleImpact CraftTimeImpact { get; set; } = TrottleImpact.Full;

        [LocCategory("Global")]
        public TrottleImpact CaloriesImpact { get; set; } = TrottleImpact.Full;
*/
        [LocCategory("Recipe groups")]
        [LocDescription("Recipe Groups settings")]
       public SerializedSynchronizedCollection<RecipeGroupSettings> RecipeGroupsSettings { get; set; } = new SerializedSynchronizedCollection<RecipeGroupSettings>();

       public void UpdateRecipeGroupsSettingsCollection (int elementsCount)
       {
            int currentElements = this.RecipeGroupsSettings.Count;

            for (int i = 0; (i < elementsCount) || (i < this.RecipeGroupsSettings.Count); i++) 
            {
                ConsoleLogWriter.Instance.Write("i = " + i + " elementsCount = " + elementsCount + " RecipeGroupSettings.Count = " + this.RecipeGroupsSettings.Count + "\n");
                if (i >= this.RecipeGroupsSettings.Count && ( i < elementsCount))
                {
                    this.RecipeGroupsSettings.Add(new RecipeGroupSettings());
                    this.RecipeGroupsSettings[i].Id = i;
                }
                else if (i < this.RecipeGroupsSettings.Count && ( i > elementsCount))
                {
                    this.RecipeGroupsSettings.RemoveAt(i);
                }
                else this.RecipeGroupsSettings[i].Id = i;
            }
            ConsoleLogWriter.Instance.Write("this.RecipeGroupsSettings.Count = " + this.RecipeGroupsSettings.Count + "\n");

       }

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

            OnParamEnableChanged();

            TimerTask timerTask = new TimerTask(TimeSpan.FromSeconds(60));
            string id = "myTimer"; // Идентификатор таймера

            timerTask.Start(() =>
            {
                OnGlobalCoefficientChanged();
                // Логика задачи, которую необходимо выполнить
                ConsoleLogWriter.Instance.Write(string.Format("The Elapsed event was raised at {0:HH:mm:ss.fff}\n", DateTime.Now));
                ConsoleLogWriter.Instance.Write(string.Format("The Elapsed event was raised at {0}\n", WorldTime.Seconds ));
            }, id);

            //UserManager.OnUserLoggedIn.Add(this.OnUserLogin);                   // Register our OnUserLoggedIn event handler for showing players our welcome message.
        }
        public Task ShutdownAsync()
        {
            //UserManager.OnUserLoggedIn.Remove(this.OnUserLogin);                // Remove our OnUserLoggedIn event handler
            return Task.CompletedTask;
        }

        public string GetDisplayTitle() => "RecipeFamily Groups list";
        public string GetDisplayText()
        {
            StringBuilder stringBuilder = new StringBuilder(1024);
            //stringBuilder.AppendLine((string)Localizer.DoStr("RecipeFamily Group:"));
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
                            RecipeThrottle.UpdateWrapValue(ingredientElement.Quantity, recipeThrottle.CalculateMultiplier(recipeFamily, MultiplierType.Quantity));
                        }
                        recept.FirePropertyChanged("Ingredients");    
                    }

                    RecipeThrottle.UpdateWrapValue(recipeFamily.CraftMinutes, recipeThrottle.CalculateMultiplier(recipeFamily, MultiplierType.CraftTime));
                    RecipeThrottle.UpdateWrapValue(recipeFamily.LaborInCalories, recipeThrottle.CalculateMultiplier(recipeFamily, MultiplierType.Callories));

                    recipeFamily.FirePropertyChanged("Recipes");
                    recipeFamily.FirePropertyChanged("Ingredients");

                    recipeFamily.FirePropertyChanged("Labor");
                    recipeFamily.FirePropertyChanged("LaborInCallories");
                    recipeFamily.FirePropertyChanged("CraftMinutes");

                    /*
                    //IEnumerable<WorldObjectItem> Tables = Enumerable.Select<Type, WorldObjectItem>(CraftingComponent.TablesForRecipe(recipeFamily.GetType()), (Func<Type, WorldObjectItem>)(table => WorldObjectItem.GetCreatingItemTemplateFromType(table))).NonNull<WorldObjectItem>();
                    foreach (Type tableItem in CraftingComponent.TablesForRecipe(recipeFamily.GetType()))
                    {
                        IEnumerable<WorldObject> tableObjects = Enumerable.Where<WorldObject>(ServiceHolder<IWorldObjectManager>.Obj.All, (Func<WorldObject, bool>)(worldObject => Type.ReferenceEquals(worldObject.GetType(), tableItem)));
                        //ConsoleLogWriter.Instance.Write("Table " + table.DisplayName + " | " + table.GetPlacementString() +".\n");
                        foreach (WorldObject tableObject in tableObjects)
                        {
                            
                            ConsoleLogWriter.Instance.Write("TableObjecte " + tableObject.DisplayName + ".\n");
                            ConsoleLogWriter.Instance.Write("tableObject.Components.Count() " + tableObject.Components.Count() + ".\n");

                            foreach (WorldObjectComponent component  in tableObject.Components)
                            {
                                if (component is CraftingComponent)
                                {
                                    CraftingComponent craftingcomponent = component as CraftingComponent;
                                   
                                    foreach (RecipeFamily rcpt in craftingcomponent.Recipes)
                                    {
                                        rcpt.FirePropertyChanged("Recipes");
                                        rcpt.FirePropertyChanged("Ingredients");
                                        rcpt.LaborInCalories.FirePropertyChanged("Values");
                                        rcpt.FirePropertyChanged("LaborInCallories");
                                        rcpt.FirePropertyChanged("Labor");

                                        
                                        rcpt.FirePropertyChanged("CraftMinutes");
                                    }
                                    ConsoleLogWriter.Instance.Write("TabtableObjectle " + craftingcomponent.Recipes.Count() + ".\n");
                                    craftingcomponent.FirePropertyChanged("ResourceEfficiencyModule");
                                    craftingcomponent.FirePropertyChanged("SpeedEfficiencyModule");
                                    craftingcomponent.FirePropertyChanged("LaborReservationModule");
                                    //craftingcomponent.FirePropertyChanged("ValidTalents");
                                    //craftingcomponent.FirePropertyChanged("Recipes");
                                }


                            }
                            

                        }

                    }*/
                    /*
                    foreach ( Item item in Item.AllItems)
                    {
                        Type type = item.GetType();
                        bool hasAttribute = type.IsDefined(typeof(CraftingComponent), false);
                        if (hasAttribute)
                        {
                            ConsoleLogWriter.Instance.Write("Table " + item.DisplayName + " | " + item.ToString() + ".\n");
                            
                        }
                        item.FirePropertyChanged("Recipes");
                        item.FirePropertyChanged("ResourceEfficiencyModule");
                        item.FirePropertyChanged("SpeedEfficiencyModule");
                        item.FirePropertyChanged("LaborReservationModule");
                    }
                    */
                }

                IEnumerable<Type> objectsFromComponent = WorldObjectManager.GetWorldObjectsFromComponent(typeof(CraftingComponent), false);
                foreach (Type worldObjectType in objectsFromComponent)
                {
                    IEnumerable<WorldObject> tableObjects = Enumerable.Where<WorldObject>(ServiceHolder<IWorldObjectManager>.Obj.All, (Func<WorldObject, bool>)(worldObject => Type.ReferenceEquals(worldObject.GetType(), worldObjectType)));
                    foreach (WorldObject tableObject in tableObjects)
                    {

                        ConsoleLogWriter.Instance.Write("TableObjecte " + tableObject.DisplayName + ".\n");
                        ConsoleLogWriter.Instance.Write("tableObject.Components.Count() " + tableObject.Components.Count() + ".\n");

                        foreach (WorldObjectComponent component in tableObject.Components)
                        {
                            if (component is CraftingComponent)
                            {
                                CraftingComponent craftingcomponent = component as CraftingComponent;

                                /*foreach (RecipeFamily rcpt in craftingcomponent.Recipes)
                                {
                                    rcpt.FirePropertyChanged("Recipes");
                                    rcpt.FirePropertyChanged("Ingredients");
                                    rcpt.LaborInCalories.FirePropertyChanged("Values");
                                    rcpt.FirePropertyChanged("LaborInCallories");
                                    rcpt.FirePropertyChanged("Labor");


                                    rcpt.FirePropertyChanged("CraftMinutes");
                                }*/
                                ConsoleLogWriter.Instance.Write("TabtableObjectle " + craftingcomponent.Recipes.Count() + ".\n");
                                craftingcomponent.FirePropertyChanged("ResourceEfficiencyModule");
                                craftingcomponent.FirePropertyChanged("SpeedEfficiencyModule");
                                craftingcomponent.FirePropertyChanged("LaborReservationModule");
                                //craftingcomponent.FirePropertyChanged("ValidTalents");
                                //craftingcomponent.FirePropertyChanged("Recipes");
                            }


                        }
                    }
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
                            RecipeThrottle.PatchIngredientElementValues(ingredientElement, recipeThrottle.CalculateMultiplier(recipeFamily, MultiplierType.Quantity));
                            i++;
                        }
                        recept.FirePropertyChanged("Ingredients");
                    }


                    RecipeThrottle.PatchRecipeFamilyValues(recipeFamily, recipeThrottle.CalculateMultiplier(recipeFamily, MultiplierType.Callories), recipeThrottle.CalculateMultiplier(recipeFamily, MultiplierType.CraftTime));
                    recipeFamily.FirePropertyChanged("Recipes");
                    recipeFamily.FirePropertyChanged("Ingredients");

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
                            RecipeThrottle.UnPatchIngredientElementValues(ingredientElement);
                            i++;
                        }
                        recept.FirePropertyChanged("Ingredients");
                    }


                    RecipeThrottle.UnPatchRecipeFamilyValues(recipeFamily);
                    recipeFamily.FirePropertyChanged("Recipes");
                    recipeFamily.FirePropertyChanged("Ingredients");

                }
                ConsoleLogWriter.Instance.Write("RecipeThrottle: Success remove wraps for " + i + " IngredientElements.\n");
                this.status = "Disabled.";
            }
        }



    }

    public class TimerTask
    {
        private readonly PeriodicTimer timer;
        private readonly CancellationTokenSource cancellationToken = new CancellationTokenSource();
        private Task? timerTask;

        public string? Identifier { get; private set; }

        public TimerTask(TimeSpan interval) => this.timer = new PeriodicTimer(interval);

        public void Start(Action routine, string id)
        {
            this.Identifier = id;
            this.timerTask = this.DoWorkAsync(routine);
        }

        private async Task DoWorkAsync(Action routine)
        {
            try
            {
                while (true)
                {
                    if (await this.timer.WaitForNextTickAsync(this.cancellationToken.Token))
                        routine();
                    else
                        break;
                }
            }
            catch (OperationCanceledException ex)
            {
                ConsoleLogWriter.Instance.Write("Timer: " + this.Identifier + " has been cancelled.");
            }
        }

        public async Task StopAsync()
        {
            if (this.timerTask == null)
                return;
            this.cancellationToken.Cancel();
            await this.timerTask;
            this.cancellationToken.Dispose();
        }
    }
}

#pragma warning restore CA1416 // Проверка совместимости платформы