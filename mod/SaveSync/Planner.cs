using System;
using System.Collections.Generic;

namespace SaveSync
{
    public static class Planner
    {
        private const double Tolerance = 0.0001;
        private const double MinimumShare = 0.5;

        public static Dictionary<string, SoftwareType> BaseSoftware()
        {
            Dictionary<string, SoftwareType> types = new Dictionary<string, SoftwareType>();
            foreach (SoftwareType type in GameData.AllSoftwareTypes(new ModPackage[0]))
            {
                types[type.Name] = type;
            }
            return types;
        }

        public static Dictionary<string, CompanyType> BaseCompanies()
        {
            Dictionary<string, CompanyType> types = new Dictionary<string, CompanyType>();
            foreach (CompanyType type in GameData.AllCompanyTypes(new ModPackage[0]))
            {
                types[type.Name] = type;
            }
            return types;
        }

        // Fills added with every part the mod adds on top of the base game, and present with those of them found in the save.
        public static void AddedParts(ModPackage package, MarketSimulation simulation, Dictionary<string, SoftwareType> baseSoftware, Dictionary<string, CompanyType> baseCompanies, Dictionary<string, bool> added, Dictionary<string, bool> present)
        {
            foreach (SoftwareType type in GameData.AllSoftwareTypes(new ModPackage[] { package }))
            {
                if (!package.SoftwareTypes.ContainsKey(type.Name) && !package.SoftwareTypeOverrides.ContainsKey(type.Name))
                {
                    continue;
                }

                SoftwareType current;
                simulation.SoftwareTypes.TryGetValue(type.Name, out current);
                SoftwareType original;
                baseSoftware.TryGetValue(type.Name, out original);

                if (original == null)
                {
                    NotePart("software|" + type.Name, current != null, added, present);
                }
                foreach (string name in type.Categories.Keys)
                {
                    if (original == null || !original.Categories.ContainsKey(name))
                    {
                        NotePart(type.Name + "|category|" + name, current != null && current.Categories.ContainsKey(name), added, present);
                    }
                }
                foreach (string name in type.Features.Keys)
                {
                    if (original == null || !original.Features.ContainsKey(name))
                    {
                        NotePart(type.Name + "|feature|" + name, current != null && current.Features.ContainsKey(name), added, present);
                    }
                }
                foreach (string name in type.AddOns.Keys)
                {
                    if (original == null || !original.AddOns.ContainsKey(name))
                    {
                        NotePart(type.Name + "|addon|" + name, current != null && current.AddOns.ContainsKey(name), added, present);
                    }
                }
            }

            foreach (string name in package.CompanyTypes.Keys)
            {
                if (!baseCompanies.ContainsKey(name))
                {
                    NotePart("company|" + name, simulation.CompanyTypes.ContainsKey(name), added, present);
                }
            }
        }

        private static void NotePart(string key, bool inSave, Dictionary<string, bool> added, Dictionary<string, bool> present)
        {
            added[key] = true;
            if (inSave)
            {
                present[key] = true;
            }
        }

        // A save keeps no list of its mods. A mod counts as used when most of what it adds on top of the base game is in the save.
        // One match is not enough: different mods often share names like a company type or a feature.
        // A mod that continues another one repeats most of its names, so the older mod looks used in a save made only with the newer one.
        // A mod is not used when everything it has in the save is also added by the other used mods. Mods that add less are checked first:
        // the continuation adds more, so it stays, and of two mods with the same content one stays.
        public static List<ModPackage> UsedPackages(MarketSimulation simulation)
        {
            Dictionary<string, SoftwareType> baseSoftware = BaseSoftware();
            Dictionary<string, CompanyType> baseCompanies = BaseCompanies();
            List<ModPackage> candidates = new List<ModPackage>();
            List<Dictionary<string, bool>> addedParts = new List<Dictionary<string, bool>>();
            List<Dictionary<string, bool>> presentParts = new List<Dictionary<string, bool>>();
            for (int i = 0; i < GameData.ModPackages.Count; i++)
            {
                ModPackage package = GameData.ModPackages[i];
                Dictionary<string, bool> added = new Dictionary<string, bool>();
                Dictionary<string, bool> present = new Dictionary<string, bool>();
                AddedParts(package, simulation, baseSoftware, baseCompanies, added, present);
                bool inSave = added.Count > 0 && (double)present.Count / added.Count >= MinimumShare;
                UnityEngine.Debug.Log("Save Sync: " + package.ItemTitle + " in save " + present.Count + "/" + added.Count + " used " + inSave);
                if (inSave)
                {
                    candidates.Add(package);
                    addedParts.Add(added);
                    presentParts.Add(present);
                }
            }

            List<int> order = new List<int>();
            for (int i = 0; i < candidates.Count; i++)
            {
                int at = 0;
                while (at < order.Count && addedParts[order[at]].Count <= addedParts[i].Count)
                {
                    at++;
                }
                order.Insert(at, i);
            }

            bool[] dropped = new bool[candidates.Count];
            for (int k = 0; k < order.Count; k++)
            {
                int i = order[k];
                if (CoveredByOthers(i, presentParts[i], addedParts, dropped))
                {
                    dropped[i] = true;
                    UnityEngine.Debug.Log("Save Sync: " + candidates[i].ItemTitle + " not used: everything it has in the save is also added by other used mods");
                }
            }

            List<ModPackage> used = new List<ModPackage>();
            for (int i = 0; i < candidates.Count; i++)
            {
                if (!dropped[i])
                {
                    used.Add(candidates[i]);
                }
            }
            return used;
        }

        private static bool CoveredByOthers(int self, Dictionary<string, bool> parts, List<Dictionary<string, bool>> addedParts, bool[] dropped)
        {
            foreach (string key in parts.Keys)
            {
                bool found = false;
                for (int j = 0; j < addedParts.Count && !found; j++)
                {
                    if (j != self && !dropped[j] && addedParts[j].ContainsKey(key))
                    {
                        found = true;
                    }
                }
                if (!found)
                {
                    return false;
                }
            }
            return true;
        }

        // Types, features and categories defined by two used mods. Their values in the save are the result of a load order the save does not record.
        // A type is listed by its name, its parts as "type|feature|name" and "type|category|name". A part only one mod defines is not a conflict.
        public static List<string> SharedTypes(List<ModPackage> used)
        {
            List<string> seen = new List<string>();
            List<string> shared = new List<string>();
            for (int i = 0; i < used.Count; i++)
            {
                List<string> names = new List<string>();
                foreach (SoftwareType type in GameData.AllSoftwareTypes(new ModPackage[] { used[i] }))
                {
                    if (!used[i].SoftwareTypes.ContainsKey(type.Name) && !used[i].SoftwareTypeOverrides.ContainsKey(type.Name))
                    {
                        continue;
                    }
                    names.Add(type.Name);
                    foreach (string feature in type.Features.Keys)
                    {
                        names.Add(type.Name + "|feature|" + feature);
                    }
                    foreach (string category in type.Categories.Keys)
                    {
                        names.Add(type.Name + "|category|" + category);
                    }
                }
                for (int j = 0; j < names.Count; j++)
                {
                    if (!seen.Contains(names[j]))
                    {
                        seen.Add(names[j]);
                    }
                    else if (!shared.Contains(names[j]))
                    {
                        shared.Add(names[j]);
                    }
                }
            }
            return shared;
        }

        private static bool HasName(IEnumerable<string> names, string name)
        {
            foreach (string item in names)
            {
                if (string.Equals(item, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        public static ModPlan Build(ModPackage package, MarketSimulation simulation, List<string> shared, List<string> choices)
        {
            ModPlan plan = new ModPlan(package.ItemTitle);
            SoftwareType[] target = ToArray(GameData.AllSoftwareTypes(new ModPackage[] { package }));

            string errors = GameData.CheckForErrors(target);
            if (!string.IsNullOrEmpty(errors))
            {
                plan.Refused = errors;
                return plan;
            }

            foreach (KeyValuePair<string, SoftwareTypeOverride> item in package.SoftwareTypeOverrides)
            {
                if (!item.Value.Delete)
                {
                    continue;
                }
                bool replaced = false;
                for (int i = 0; i < target.Length; i++)
                {
                    replaced = replaced || target[i].Name == item.Key;
                }
                plan.NotApplied.Add(replaced
                    ? item.Key + Texts.L(": the mod replaces this type in a new game. Here the old content stays and only new content is added", ": мод заменяет этот тип в новой игре. Здесь старое содержимое останется, добавится только новое")
                    : Texts.L("Type stays in the save: ", "Тип останется в сейве: ") + item.Key);
            }

            Dictionary<string, RandomNameGenerator> generators = new Dictionary<string, RandomNameGenerator>(simulation.RNG);
            foreach (KeyValuePair<string, RandomNameGenerator> item in package.NameGenerators)
            {
                generators[item.Key] = item.Value;
            }
            bool brokenCategories = false;

            for (int i = 0; i < target.Length; i++)
            {
                SoftwareType type = target[i];
                if (!package.SoftwareTypes.ContainsKey(type.Name) && !package.SoftwareTypeOverrides.ContainsKey(type.Name))
                {
                    continue;
                }

                int before = plan.Added.Count + plan.Changed.Count;
                SoftwareType current;
                if (simulation.SoftwareTypes.TryGetValue(type.Name, out current))
                {
                    bool typeShared = shared.Contains(type.Name) && !choices.Contains(type.Name + "|" + package.ItemTitle);
                    PlanExistingType(plan, simulation, current, type, generators, typeShared, typeShared ? shared : new List<string>());
                    brokenCategories = brokenCategories || HasBrokenCategory(current, simulation.RNG);
                }
                else
                {
                    PlanNewType(plan, simulation, type, generators);
                }
                if (plan.Added.Count + plan.Changed.Count > before)
                {
                    plan.Touched.Add(type.Name);
                }
            }

            DropMissingDependencies(plan, simulation);
            DropChildrenOfRefusedFeatures(plan, simulation, target);
            PlanCompanies(plan, package, simulation, generators);
            PlanPersonalities(plan, package);
            if (plan.NewCategories.Count > 0)
            {
                plan.NotApplied.Add(Texts.L("New categories keep the default submarket split: the market sets it up only in a new game", "У новых категорий доли подрынков останутся стандартными: рынок задаёт их только в новой игре"));
            }
            WarnAboutTypesWithoutCompanies(plan, simulation);
            PlanGenerators(plan, package, simulation, brokenCategories);
            return plan;
        }

        // The market calls GetNameGen while releasing a product. A missing generator there throws out of the monthly simulation.
        public static bool GeneratorKnown(SoftwareCategory category, Dictionary<string, RandomNameGenerator> generators)
        {
            try
            {
                category.GetNameGen(generators);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool HasBrokenCategory(SoftwareType type, Dictionary<string, RandomNameGenerator> generators)
        {
            foreach (SoftwareCategory category in type.Categories.Values)
            {
                if (!GeneratorKnown(category, generators))
                {
                    return true;
                }
            }
            return false;
        }

        private static void PlanGenerators(ModPlan plan, ModPackage package, MarketSimulation simulation, bool brokenCategories)
        {
            if (!brokenCategories && plan.NewTypes.Count == 0 && plan.NewCategories.Count == 0 && plan.NewAddOns.Count == 0 && plan.NewCompanyTypes.Count == 0)
            {
                return;
            }
            foreach (KeyValuePair<string, RandomNameGenerator> item in package.NameGenerators)
            {
                if (!simulation.RNG.ContainsKey(item.Key))
                {
                    plan.NewGenerators[item.Key] = item.Value;
                }
            }
            if (plan.NewGenerators.Count > 0)
            {
                plan.Added.Add(Texts.L("Name generators: ", "Генераторы имён: ") + plan.NewGenerators.Count);
            }
        }

        private static void PlanNewType(ModPlan plan, MarketSimulation simulation, SoftwareType type, Dictionary<string, RandomNameGenerator> generators)
        {
            foreach (KeyValuePair<string, SoftwareCategory> item in type.Categories)
            {
                if (item.Value.Hardware)
                {
                    plan.NotApplied.Add(Texts.L("Hardware is not supported yet: ", "Железо пока не поддерживается: ") + type.Name);
                    return;
                }
                if (!GeneratorKnown(item.Value, generators))
                {
                    plan.NotApplied.Add(Texts.L("Name generator is missing: ", "Нет генератора имён: ") + type.Name + " / " + item.Key);
                    return;
                }
            }
            foreach (KeyValuePair<string, FeatureBase> item in type.Features)
            {
                if (!SpecKnown(simulation, item.Value.Spec))
                {
                    plan.NotApplied.Add(Texts.L("New tech specializations are not supported yet: ", "Новые специализации пока не поддерживаются: ") + type.Name);
                    return;
                }
            }
            foreach (KeyValuePair<string, SoftwareAddOn> item in type.AddOns)
            {
                if (!AddOnGeneratorKnown(item.Value, generators))
                {
                    plan.NotApplied.Add(Texts.L("Name generator is missing: ", "Нет генератора имён: ") + type.Name + " / " + item.Key);
                    return;
                }
            }
            plan.NewTypes.Add(type);
            plan.Added.Add(Texts.L("Type ", "Тип ") + type.Name);
        }

        private static void PlanExistingType(ModPlan plan, MarketSimulation simulation, SoftwareType current, SoftwareType target, Dictionary<string, RandomNameGenerator> generators, bool shared, List<string> sharedParts)
        {
            ModPlan numbers = shared ? new ModPlan(plan.Title) : plan;
            int otherFeatures = 0;
            int otherCategories = 0;

            foreach (KeyValuePair<string, SoftwareCategory> item in target.Categories)
            {
                SoftwareCategory existing;
                if (current.Categories.TryGetValue(item.Key, out existing))
                {
                    bool partShared = sharedParts.Contains(target.Name + "|category|" + item.Key);
                    PlanCategoryChange(partShared ? numbers : plan, partShared ? item.Key : target.Name + " / " + item.Key, existing, item.Value);
                    continue;
                }
                if (HasName(current.Categories.Keys, item.Key))
                {
                    continue;
                }
                if (item.Value.Hardware)
                {
                    plan.NotApplied.Add(Texts.L("Hardware is not supported yet: ", "Железо пока не поддерживается: ") + target.Name + " / " + item.Key);
                    continue;
                }
                if (!GeneratorKnown(item.Value, generators))
                {
                    plan.NotApplied.Add(Texts.L("Name generator is missing: ", "Нет генератора имён: ") + target.Name + " / " + item.Key);
                    continue;
                }
                CategoryAddition addition = new CategoryAddition();
                addition.Owner = current;
                addition.Category = item.Value;
                plan.NewCategories.Add(addition);
                plan.Added.Add(Texts.L("Category ", "Категория ") + target.Name + " / " + item.Key);
            }

            int frozenFeatures = 0;
            foreach (KeyValuePair<string, FeatureBase> item in target.Features)
            {
                FeatureBase existing;
                if (current.Features.TryGetValue(item.Key, out existing))
                {
                    bool partShared = sharedParts.Contains(target.Name + "|feature|" + item.Key);
                    if (!partShared && FeatureFrozenDiffers(existing, item.Value))
                    {
                        frozenFeatures++;
                    }
                    PlanFeatureChange(partShared ? numbers : plan, partShared ? item.Key : target.Name + " / " + item.Key, existing, item.Value);
                    continue;
                }
                if (HasName(current.Features.Keys, item.Key))
                {
                    continue;
                }
                bool unknownSpec = !SpecKnown(simulation, item.Value.Spec);
                bool takenSpec = !unknownSpec && item.Value is SpecFeature && MainSpecTaken(plan, current, item.Value.Spec);
                if (unknownSpec || takenSpec)
                {
                    if (unknownSpec)
                    {
                        plan.NotApplied.Add(Texts.L("New tech specializations are not supported yet: ", "Новые специализации пока не поддерживаются: ") + target.Name + " / " + item.Key);
                    }
                    else
                    {
                        plan.NotApplied.Add(target.Name + " / " + item.Key + Texts.L(": the type already has a main feature with the specialization ", ": у типа уже есть главная фича со специализацией ") + item.Value.Spec);
                    }
                    if (!plan.RefusedFeatures.ContainsKey(target.Name))
                    {
                        plan.RefusedFeatures[target.Name] = new List<string>();
                    }
                    plan.RefusedFeatures[target.Name].Add(item.Key);
                    continue;
                }
                FeatureAddition addition = new FeatureAddition();
                addition.Owner = current;
                addition.Feature = item.Value;
                plan.NewFeatures.Add(addition);
                plan.Added.Add(Texts.L("Feature ", "Фича ") + target.Name + " / " + item.Key);
            }

            if (frozenFeatures > 0)
            {
                plan.NotApplied.Add(target.Name + ": " + Texts.L("DevTime, Server, CodeArt, Forced or ForceCats of features stay as they are: ", "DevTime, Server, CodeArt, Forced и ForceCats фич останутся прежними: ") + frozenFeatures);
            }

            PlanSubmarketNames(numbers, shared ? null : target.Name, current, target, plan);

            if (shared)
            {
                if (numbers.Changed.Count > 0)
                {
                    plan.Conflicts[target.Name] = numbers.Changed;
                    plan.NotApplied.Add(target.Name + Texts.L(": other mods change this type too, you will choose whose numbers to use", ": этот тип меняют и другие моды, ты выберешь, чьи числа брать"));
                }
            }
            else
            {
                List<string> frozen = new List<string>();
                if (current.Unlock != target.Unlock)
                {
                    frozen.Add("Unlock");
                }
                if (Differs(current.OptimalDevTime, target.OptimalDevTime))
                {
                    frozen.Add("OptimalDevTime");
                }
                if (Differs(current.RandomFactor, target.RandomFactor))
                {
                    frozen.Add("Random");
                }
                if (current.OneClient != target.OneClient)
                {
                    frozen.Add("OneClient");
                }
                if (current.InHouse != target.InHouse)
                {
                    frozen.Add("InHouse");
                }
                if (!SameNames(ToList(current.GetOSLimits()), ToList(target.GetOSLimits())))
                {
                    frozen.Add("OSSupport");
                }
                ReportFrozen(plan, target.Name, frozen);
            }

            foreach (string name in current.Features.Keys)
            {
                if (!HasName(target.Features.Keys, name))
                {
                    otherFeatures++;
                }
            }
            foreach (string name in current.Categories.Keys)
            {
                if (!HasName(target.Categories.Keys, name))
                {
                    otherCategories++;
                }
            }
            if (otherFeatures > 0)
            {
                plan.NotApplied.Add(target.Name + Texts.L(": features in the save but not in this mod stay: ", ": фичи из сейва, которых нет в моде, останутся: ") + otherFeatures);
            }
            if (otherCategories > 0)
            {
                plan.NotApplied.Add(target.Name + Texts.L(": categories in the save but not in this mod stay: ", ": категории из сейва, которых нет в моде, останутся: ") + otherCategories);
            }

            foreach (KeyValuePair<string, SoftwareAddOn> item in target.AddOns)
            {
                if (!HasName(current.AddOns.Keys, item.Key))
                {
                    PlanAddOn(plan, simulation, current, target, item.Value, generators);
                }
            }
        }

        private static void PlanSubmarketNames(ModPlan numbers, string label, SoftwareType current, SoftwareType target, ModPlan plan)
        {
            if (current.SubMarkets == null || target.SubMarkets == null || SameOrder(current.SubMarkets, target.SubMarkets))
            {
                return;
            }
            if (current.SubMarkets.Length != target.SubMarkets.Length)
            {
                plan.NotApplied.Add(target.Name + Texts.L(": the number of submarkets stays as it is", ": число сабмаркетов останется прежним"));
                return;
            }
            TypeChange change = new TypeChange();
            change.Current = current;
            change.Target = target;
            numbers.TypeChanges.Add(change);
            numbers.Changed.Add((label != null ? label + ": " : "") + "SubmarketNames " + string.Join(" ", current.SubMarkets) + " -> " + string.Join(" ", target.SubMarkets));
        }

        // Parents are checked after all features are planned, in DropAddOnsWithoutParents.
        private static void PlanAddOn(ModPlan plan, MarketSimulation simulation, SoftwareType current, SoftwareType target, SoftwareAddOn addOn, Dictionary<string, RandomNameGenerator> generators)
        {
            string label = target.Name + " / " + addOn.Name;
            if (addOn.Hardware)
            {
                plan.NotApplied.Add(Texts.L("Hardware is not supported yet: ", "Железо пока не поддерживается: ") + label);
                return;
            }
            if (!AddOnGeneratorKnown(addOn, generators))
            {
                plan.NotApplied.Add(Texts.L("Name generator is missing: ", "Нет генератора имён: ") + label);
                return;
            }
            foreach (AddOnFeature feature in addOn.Features.Values)
            {
                if (!SpecKnown(simulation, feature.Spec))
                {
                    plan.NotApplied.Add(Texts.L("New tech specializations are not supported yet: ", "Новые специализации пока не поддерживаются: ") + label);
                    return;
                }
            }
            if (addOn.Categories != null)
            {
                for (int i = 0; i < addOn.Categories.Length; i++)
                {
                    if (!current.Categories.ContainsKey(addOn.Categories[i]) && !CategoryPlanned(plan, current, addOn.Categories[i]))
                    {
                        plan.NotApplied.Add(Texts.L("Add-on needs a category that is not in the save: ", "Аддону нужна категория, которой нет в сейве: ") + label);
                        return;
                    }
                }
            }
            AddOnAddition addition = new AddOnAddition();
            addition.Owner = current;
            addition.AddOn = addOn;
            plan.NewAddOns.Add(addition);
            plan.Added.Add(Texts.L("Add-on ", "Аддон ") + label);
        }

        private static bool AddOnGeneratorKnown(SoftwareAddOn addOn, Dictionary<string, RandomNameGenerator> generators)
        {
            try
            {
                addOn.GetNameGen(generators);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // Main features are SpecFeature, one per specialization. The design window builds a dictionary by specialization and throws on a second one.
        private static bool MainSpecTaken(ModPlan plan, SoftwareType owner, string spec)
        {
            foreach (FeatureBase feature in owner.Features.Values)
            {
                if (feature is SpecFeature && feature.Spec == spec)
                {
                    return true;
                }
            }
            for (int i = 0; i < plan.NewFeatures.Count; i++)
            {
                if (plan.NewFeatures[i].Owner == owner && plan.NewFeatures[i].Feature is SpecFeature && plan.NewFeatures[i].Feature.Spec == spec)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool CategoryPlanned(ModPlan plan, SoftwareType owner, string name)
        {
            for (int i = 0; i < plan.NewCategories.Count; i++)
            {
                if (plan.NewCategories[i].Owner == owner && plan.NewCategories[i].Category.Name == name)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool FeaturePlanned(ModPlan plan, SoftwareType owner, string name)
        {
            for (int i = 0; i < plan.NewFeatures.Count; i++)
            {
                if (plan.NewFeatures[i].Owner == owner && plan.NewFeatures[i].Feature.Name == name)
                {
                    return true;
                }
            }
            return false;
        }

        // Dependencies name other software types. Runs after every type is planned, and repeats because dropping a type can orphan another.
        private static void DropMissingDependencies(ModPlan plan, MarketSimulation simulation)
        {
            bool dropped = true;
            while (dropped)
            {
                dropped = false;
                for (int i = plan.NewTypes.Count - 1; i >= 0; i--)
                {
                    string missing = FirstMissingSoftware(plan, simulation, plan.NewTypes[i]);
                    if (missing != null)
                    {
                        string name = plan.NewTypes[i].Name;
                        plan.NewTypes.RemoveAt(i);
                        plan.Added.Remove(Texts.L("Type ", "Тип ") + name);
                        plan.NotApplied.Add(name + Texts.L(": needs software that is not in the save: ", ": нужно ПО, которого нет в сейве: ") + missing);
                        dropped = true;
                    }
                }
            }

            for (int i = plan.NewFeatures.Count - 1; i >= 0; i--)
            {
                FeatureAddition addition = plan.NewFeatures[i];
                string missing = FirstMissingSoftware(plan, simulation, addition.Feature);
                if (missing != null)
                {
                    string label = addition.Owner.Name + " / " + addition.Feature.Name;
                    plan.NewFeatures.RemoveAt(i);
                    plan.Added.Remove(Texts.L("Feature ", "Фича ") + label);
                    plan.NotApplied.Add(label + Texts.L(": needs software that is not in the save: ", ": нужно ПО, которого нет в сейве: ") + missing);
                    if (!plan.RefusedFeatures.ContainsKey(addition.Owner.Name))
                    {
                        plan.RefusedFeatures[addition.Owner.Name] = new List<string>();
                    }
                    plan.RefusedFeatures[addition.Owner.Name].Add(addition.Feature.Name);
                }
            }

            for (int i = plan.DependencyChanges.Count - 1; i >= 0; i--)
            {
                DependencyChange change = plan.DependencyChanges[i];
                string missing = FirstMissingSoftware(plan, simulation, change.Target);
                if (missing != null)
                {
                    plan.DependencyChanges.RemoveAt(i);
                    plan.Changed.Remove(change.Line);
                    plan.NotApplied.Add(change.Line + Texts.L(": needs software that is not in the save: ", ": нужно ПО, которого нет в сейве: ") + missing);
                }
            }
        }

        private static string FirstMissingSoftware(ModPlan plan, MarketSimulation simulation, SoftwareType type)
        {
            foreach (FeatureBase feature in type.Features.Values)
            {
                string missing = FirstMissingSoftware(plan, simulation, feature);
                if (missing != null && missing != type.Name)
                {
                    return missing;
                }
            }
            return null;
        }

        private static string FirstMissingSoftware(ModPlan plan, MarketSimulation simulation, FeatureBase feature)
        {
            SpecFeature spec = feature as SpecFeature;
            if (spec == null || spec.Dependencies == null)
            {
                return null;
            }
            for (int i = 0; i < spec.Dependencies.Length; i++)
            {
                if (!SoftwareKnown(plan, simulation, spec.Dependencies[i]))
                {
                    return spec.Dependencies[i];
                }
            }
            return null;
        }

        // A sub feature is tied to its main feature only by specialization.
        // Children of a refused main feature land under the save's main feature of the same specialization.
        // Without such a main feature they would hang under nothing, so only those children are dropped.
        private static void DropChildrenOfRefusedFeatures(ModPlan plan, MarketSimulation simulation, SoftwareType[] target)
        {
            foreach (KeyValuePair<string, List<string>> item in plan.RefusedFeatures)
            {
                SoftwareType current;
                SoftwareType type = null;
                for (int i = 0; i < target.Length; i++)
                {
                    if (target[i].Name == item.Key)
                    {
                        type = target[i];
                    }
                }
                if (type == null || !simulation.SoftwareTypes.TryGetValue(item.Key, out current))
                {
                    continue;
                }
                List<string> orphanSpecs = new List<string>();
                for (int i = 0; i < item.Value.Count; i++)
                {
                    FeatureBase feature;
                    if (type.Features.TryGetValue(item.Value[i], out feature) && feature is SpecFeature
                        && !orphanSpecs.Contains(feature.Spec) && !MainSpecTaken(plan, current, feature.Spec))
                    {
                        orphanSpecs.Add(feature.Spec);
                    }
                }
                if (orphanSpecs.Count > 0)
                {
                    DropFeaturesOfSpecs(plan, item.Key, orphanSpecs);
                }
            }
            DropAddOnsWithoutParents(plan);
        }

        private static string DependencyText(string[] names)
        {
            return names.Length > 0 ? string.Join(", ", names) : Texts.L("none", "нет");
        }

        private static void DropFeaturesOfSpecs(ModPlan plan, string typeName, List<string> specs)
        {
            int dropped = 0;
            for (int i = plan.NewFeatures.Count - 1; i >= 0; i--)
            {
                FeatureAddition addition = plan.NewFeatures[i];
                if (addition.Owner.Name == typeName && specs.Contains(addition.Feature.Spec))
                {
                    plan.Added.Remove(Texts.L("Feature ", "Фича ") + typeName + " / " + addition.Feature.Name);
                    plan.NewFeatures.RemoveAt(i);
                    dropped++;
                }
            }
            if (dropped > 0)
            {
                plan.NotApplied.Add(typeName + Texts.L(": features without a main feature in the save are not added: ", ": фичи без главной фичи в сейве не добавятся: ") + dropped + " (" + string.Join(", ", specs.ToArray()) + ")");
            }
        }

        private static void DropAddOnsWithoutParents(ModPlan plan)
        {
            for (int i = plan.NewAddOns.Count - 1; i >= 0; i--)
            {
                AddOnAddition addition = plan.NewAddOns[i];
                string missing = MissingAddOnParent(plan, addition.Owner, addition.AddOn);
                if (missing != null)
                {
                    string label = addition.Owner.Name + " / " + addition.AddOn.Name;
                    plan.Added.Remove(Texts.L("Add-on ", "Аддон ") + label);
                    plan.NewAddOns.RemoveAt(i);
                    plan.NotApplied.Add(Texts.L("Add-on needs a feature that is not in the save: ", "Аддону нужна фича, которой нет в сейве: ") + label + " (" + missing + ")");
                }
            }
        }

        // An add-on feature names its type feature in FeatureDependency, and the base feature needs its specialization in the type.
        private static string MissingAddOnParent(ModPlan plan, SoftwareType owner, SoftwareAddOn addOn)
        {
            foreach (AddOnFeature feature in addOn.Features.Values)
            {
                string parent = feature.FeatureDependency;
                if (!string.IsNullOrEmpty(parent) && !owner.Features.ContainsKey(parent) && !FeaturePlanned(plan, owner, parent))
                {
                    return parent;
                }
            }
            if (addOn.BaseFeature != null && !string.IsNullOrEmpty(addOn.BaseFeature.Spec) && !MainSpecTaken(plan, owner, addOn.BaseFeature.Spec))
            {
                return addOn.BaseFeature.Spec;
            }
            return null;
        }

        private static List<string> ToList(IEnumerable<string> names)
        {
            return names != null ? new List<string>(names) : new List<string>();
        }

        private static bool SameNames(List<string> a, List<string> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }
            for (int i = 0; i < a.Count; i++)
            {
                if (!b.Contains(a[i]))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool SameOrder(string[] a, string[] b)
        {
            if (a == null || b == null)
            {
                return a == b;
            }
            if (a.Length != b.Length)
            {
                return false;
            }
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }
            return true;
        }

        private static void PlanCategoryChange(ModPlan plan, string label, SoftwareCategory current, SoftwareCategory target)
        {
            List<string> changed = new List<string>();
            List<string> frozen = new List<string>();
            if (Differs(current.Popularity, target.Popularity))
            {
                changed.Add("Popularity " + current.Popularity + " -> " + target.Popularity);
            }
            if (current.Unlock != target.Unlock)
            {
                changed.Add("Unlock " + current.Unlock + " -> " + target.Unlock);
            }
            if (current.LagBehind != target.LagBehind)
            {
                changed.Add("LagBehind " + current.LagBehind + " -> " + target.LagBehind);
            }
            if (current.Hidden != target.Hidden)
            {
                changed.Add("Hidden " + current.Hidden + " -> " + target.Hidden);
            }
            if (SameLength(current.SubmarketEq, target.SubmarketEq))
            {
                if (Differs(current.SubmarketEq, target.SubmarketEq))
                {
                    changed.Add("Submarkets " + Numbers(current.SubmarketEq) + " -> " + Numbers(target.SubmarketEq));
                }
            }
            else
            {
                frozen.Add("Submarkets");
            }

            if (Differs(current.TimeScale, target.TimeScale))
            {
                frozen.Add("TimeScale");
            }
            if (Differs(current.IdealPrice, target.IdealPrice))
            {
                frozen.Add("IdealPrice");
            }
            if (Differs(current.Retention, target.Retention))
            {
                frozen.Add("Retention");
            }
            if (Differs(current.Iterative, target.Iterative))
            {
                frozen.Add("Iterative");
            }

            if (changed.Count > 0)
            {
                CategoryChange change = new CategoryChange();
                change.Current = current;
                change.Target = target;
                plan.CategoryChanges.Add(change);
                plan.Changed.Add(label + ": " + string.Join(", ", changed.ToArray()));
            }
            ReportFrozen(plan, label, frozen);
        }

        private static void PlanFeatureChange(ModPlan plan, string label, FeatureBase current, FeatureBase target)
        {
            List<string> changed = new List<string>();
            if (SameLength(current.Submarkets, target.Submarkets) && Differs(current.Submarkets, target.Submarkets))
            {
                changed.Add("Submarkets " + Numbers(current.Submarkets) + " -> " + Numbers(target.Submarkets));
            }
            if (CategoryLinksDiffer(current, target))
            {
                changed.Add("SoftwareCategories");
            }
            if (changed.Count > 0)
            {
                FeatureChange change = new FeatureChange();
                change.Current = current;
                change.Target = target;
                plan.FeatureChanges.Add(change);
                plan.Changed.Add(label + ": " + string.Join(", ", changed.ToArray()));
            }
            PlanDependencyChange(plan, label, current as SpecFeature, target as SpecFeature);
        }

        private static void PlanDependencyChange(ModPlan plan, string label, SpecFeature current, SpecFeature target)
        {
            if (current == null || target == null || current.Dependencies == null || target.Dependencies == null)
            {
                return;
            }
            if (SameNames(new List<string>(current.Dependencies), new List<string>(target.Dependencies)))
            {
                return;
            }
            string line = label + ": Dependencies " + DependencyText(current.Dependencies) + " -> " + DependencyText(target.Dependencies);
            if (current.Dependencies.Length != target.Dependencies.Length)
            {
                plan.NotApplied.Add(line + Texts.L(": the number of dependencies cannot change", ": число зависимостей менять нельзя"));
                return;
            }
            DependencyChange change = new DependencyChange();
            change.Current = current;
            change.Target = target;
            change.Line = line;
            plan.DependencyChanges.Add(change);
            plan.Changed.Add(line);
        }

        private static bool CategoryLinksDiffer(FeatureBase current, FeatureBase target)
        {
            if (target.SoftwareCategories == null || current.SoftwareCategories == null)
            {
                return false;
            }
            foreach (KeyValuePair<string, int> item in target.SoftwareCategories)
            {
                int year;
                if (!current.SoftwareCategories.TryGetValue(item.Key, out year) || year != item.Value)
                {
                    return true;
                }
            }
            return false;
        }

        private static string Numbers(double[] values)
        {
            List<string> text = new List<string>();
            for (int i = 0; i < values.Length; i++)
            {
                text.Add(values[i].ToString("0.##"));
            }
            return string.Join(" ", text.ToArray());
        }

        private static void ReportFrozen(ModPlan plan, string label, List<string> frozen)
        {
            if (frozen.Count > 0)
            {
                plan.NotApplied.Add(label + Texts.L(": will not change: ", ": не изменится: ") + string.Join(", ", frozen.ToArray()));
            }
        }

        private static void PlanCompanies(ModPlan plan, ModPackage package, MarketSimulation simulation, Dictionary<string, RandomNameGenerator> generators)
        {
            for (int i = 0; i < package.DeleteCompanyTypes.Length; i++)
            {
                plan.NotApplied.Add(Texts.L("Company type stays in the save: ", "Тип компании останется в сейве: ") + package.DeleteCompanyTypes[i]);
            }

            CompanyType[] target = ToArray(GameData.AllCompanyTypes(package));
            for (int i = 0; i < target.Length; i++)
            {
                CompanyType type = target[i];
                if (!package.CompanyTypes.ContainsKey(type.Name))
                {
                    continue;
                }

                CompanyType current;
                if (simulation.CompanyTypes.TryGetValue(type.Name, out current))
                {
                    PlanExistingCompanyType(plan, simulation, current, type);
                    continue;
                }

                if (!string.IsNullOrEmpty(type.NameGen) && !generators.ContainsKey(type.NameGen))
                {
                    plan.NotApplied.Add(Texts.L("Name generator is missing: ", "Нет генератора имён: ") + type.Name);
                }
                else if (AllSoftwareKnown(plan, simulation, type))
                {
                    RemoveUnknownAddOns(plan, simulation, type);
                    plan.NewCompanyTypes.Add(type);
                    plan.Added.Add(Texts.L("Company type ", "Тип компании ") + type.Name);
                }
                else
                {
                    plan.NotApplied.Add(Texts.L("Company type needs software that is not in the save: ", "Типу компании нужно ПО, которого нет в сейве: ") + type.Name);
                }
            }
        }

        private static void PlanExistingCompanyType(ModPlan plan, MarketSimulation simulation, CompanyType current, CompanyType target)
        {
            bool changed = false;
            ReportFrozenCompanyFields(plan, current, target);
            Dictionary<KeyValuePair<string, string>, float> addons = target.Addons != null ? target.Addons : new Dictionary<KeyValuePair<string, string>, float>();
            foreach (KeyValuePair<KeyValuePair<string, string>, float> item in addons)
            {
                if (current.Addons == null || current.Addons.ContainsKey(item.Key) || !AddOnKnown(plan, simulation, item.Key.Key, item.Key.Value))
                {
                    continue;
                }
                ChanceAddition addition = new ChanceAddition();
                addition.Owner = current;
                addition.Key = item.Key;
                addition.Chance = item.Value;
                plan.NewAddOnChances.Add(addition);
                plan.Added.Add(Texts.L("Company ", "Компания ") + target.Name + Texts.L(" makes add-ons for ", " делает аддоны к ") + item.Key.Key);
            }
            foreach (KeyValuePair<KeyValuePair<string, string>, float> item in target.Types)
            {
                if (current.Types.ContainsKey(item.Key) || !SoftwareKnown(plan, simulation, item.Key.Key))
                {
                    continue;
                }
                ChanceAddition addition = new ChanceAddition();
                addition.Owner = current;
                addition.Key = item.Key;
                addition.Chance = item.Value;
                plan.NewChances.Add(addition);
                plan.Added.Add(Texts.L("Company ", "Компания ") + target.Name + Texts.L(" makes ", " делает ") + item.Key.Key);
                changed = true;
            }
            if (changed || plan.NewCategories.Count > 0 || plan.NewTypes.Count > 0)
            {
                plan.CompanyTargets.Add(target);
            }
        }

        private static bool AddOnKnown(ModPlan plan, MarketSimulation simulation, string software, string addOn)
        {
            SoftwareType type;
            if (simulation.SoftwareTypes.TryGetValue(software, out type) && type.AddOns.ContainsKey(addOn))
            {
                return true;
            }
            for (int i = 0; i < plan.NewAddOns.Count; i++)
            {
                if (plan.NewAddOns[i].Owner.Name == software && plan.NewAddOns[i].AddOn.Name == addOn)
                {
                    return true;
                }
            }
            for (int i = 0; i < plan.NewTypes.Count; i++)
            {
                if (plan.NewTypes[i].Name == software && plan.NewTypes[i].AddOns.ContainsKey(addOn))
                {
                    return true;
                }
            }
            return false;
        }

        // The company type here is a throwaway copy, so entries the save cannot back are removed from it.
        private static void RemoveUnknownAddOns(ModPlan plan, MarketSimulation simulation, CompanyType type)
        {
            if (type.Addons == null)
            {
                return;
            }
            List<KeyValuePair<string, string>> keys = new List<KeyValuePair<string, string>>(type.Addons.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                if (!AddOnKnown(plan, simulation, keys[i].Key, keys[i].Value))
                {
                    type.Addons.Remove(keys[i]);
                    plan.NotApplied.Add(type.Name + Texts.L(": add-on is not in the save: ", ": аддона нет в сейве: ") + keys[i].Key + " / " + keys[i].Value);
                }
            }
        }

        private static void ReportFrozenCompanyFields(ModPlan plan, CompanyType current, CompanyType target)
        {
            List<string> frozen = new List<string>();
            if (current.Min != target.Min)
            {
                frozen.Add("Min");
            }
            if (current.Max != target.Max)
            {
                frozen.Add("Max");
            }
            if (Differs(current.PerYear, target.PerYear))
            {
                frozen.Add("PerYear");
            }
            if (current.Frameworks != target.Frameworks)
            {
                frozen.Add("Frameworks");
            }
            if (current.LatestTech != target.LatestTech)
            {
                frozen.Add("LatestTech");
            }
            ReportFrozen(plan, target.Name, frozen);
        }

        private static void PlanPersonalities(ModPlan plan, ModPackage package)
        {
            PersonalityGraph graph = package.Personalities;
            if (graph == null || graph.PersonalityTraits == null || GameSettings.Instance == null || GameSettings.Instance.Personalities == null)
            {
                return;
            }
            List<string> saved = GameSettings.Instance.Personalities.PersonalityTraits;
            int missing = 0;
            for (int i = 0; i < graph.PersonalityTraits.Count; i++)
            {
                if (saved == null || !saved.Contains(graph.PersonalityTraits[i]))
                {
                    missing++;
                }
            }
            if (missing == 0)
            {
                return;
            }
            if (graph.Replace)
            {
                plan.NotApplied.Add(Texts.L("Personalities replace the game's own set, which cannot change in a running save", "Характеры заменяют набор игры, а в идущем сейве его менять нельзя"));
                return;
            }
            plan.Personalities = graph;
            plan.Added.Add(Texts.L("Personality traits: ", "Черты характера: ") + missing);
        }

        private static void WarnAboutTypesWithoutCompanies(ModPlan plan, MarketSimulation simulation)
        {
            for (int i = 0; i < plan.NewTypes.Count; i++)
            {
                string name = plan.NewTypes[i].Name;
                bool made = false;
                foreach (CompanyType type in simulation.CompanyTypes.Values)
                {
                    made = made || MakesSoftware(type, name);
                }
                for (int j = 0; j < plan.NewCompanyTypes.Count; j++)
                {
                    made = made || MakesSoftware(plan.NewCompanyTypes[j], name);
                }
                for (int j = 0; j < plan.NewChances.Count; j++)
                {
                    made = made || plan.NewChances[j].Key.Key == name;
                }
                if (!made)
                {
                    plan.Warnings.Add(Texts.L("No company will release ", "Ни одна компания не будет выпускать ") + name);
                }
            }
        }

        private static bool MakesSoftware(CompanyType type, string software)
        {
            foreach (KeyValuePair<string, string> key in type.Types.Keys)
            {
                if (key.Key == software)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool AllSoftwareKnown(ModPlan plan, MarketSimulation simulation, CompanyType type)
        {
            foreach (KeyValuePair<string, string> key in type.Types.Keys)
            {
                if (!SoftwareKnown(plan, simulation, key.Key))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool SoftwareKnown(ModPlan plan, MarketSimulation simulation, string name)
        {
            if (simulation.SoftwareTypes.ContainsKey(name))
            {
                return true;
            }
            for (int i = 0; i < plan.NewTypes.Count; i++)
            {
                if (plan.NewTypes[i].Name == name)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool SpecKnown(MarketSimulation simulation, string spec)
        {
            if (string.IsNullOrEmpty(spec))
            {
                return true;
            }
            return simulation.TechLevels.ContainsKey(spec)
                && simulation.MarketTechLevels.ContainsKey(spec)
                && simulation.TechComplexity.ContainsKey(spec);
        }

        private static bool FeatureFrozenDiffers(FeatureBase a, FeatureBase b)
        {
            if (Differs(a.DevTime, b.DevTime) || Differs(a.ServerRequirement, b.ServerRequirement) || Differs(a.CodeArtRatio, b.CodeArtRatio))
            {
                return true;
            }
            SpecFeature first = a as SpecFeature;
            SpecFeature second = b as SpecFeature;
            if (first == null || second == null)
            {
                return false;
            }
            return first.Forced != second.Forced || !SameOrder(first.ForceCats, second.ForceCats);
        }

        private static bool Differs(double a, double b)
        {
            return Math.Abs(a - b) > Tolerance;
        }

        private static bool Differs(double[] a, double[] b)
        {
            for (int i = 0; i < a.Length; i++)
            {
                if (Differs(a[i], b[i]))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool SameLength(double[] a, double[] b)
        {
            return a != null && b != null && a.Length == b.Length;
        }

        private static T[] ToArray<T>(IEnumerable<T> items)
        {
            return new List<T>(items).ToArray();
        }
    }
}
