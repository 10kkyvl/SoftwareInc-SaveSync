using System.Collections.Generic;

namespace SaveSync
{
    public static class Merger
    {
        public static void Apply(ModPlan plan, MarketSimulation simulation)
        {
            AddGenerators(plan, simulation);
            AddTypes(plan, simulation);
            AddCategories(plan, simulation);
            AddFeatures(plan);
            AddAddOns(plan);
            UpdateTypes(plan);
            UpdateCategories(plan, simulation);
            UpdateFeatures(plan);
            UpdateDependencies(plan);
            AddCompanyTypes(plan, simulation);
            AddChances(plan);
            AddAddOnChances(plan);
            UpdateCompanies(plan, simulation);
            MergePersonalities(plan);
        }

        // The save holds its own personality graph. MergeWith appends incompatibilities again on a second call,
        // so it runs only while the mod still has traits the save lacks.
        private static void MergePersonalities(ModPlan plan)
        {
            if (plan.Personalities != null && GameSettings.Instance != null && GameSettings.Instance.Personalities != null)
            {
                GameSettings.Instance.Personalities.MergeWith(plan.Personalities);
            }
        }

        public static void RefreshSpecializations(MarketSimulation simulation)
        {
            List<SoftwareType> types = new List<SoftwareType>(simulation.SoftwareTypes.Values);
            string[][] specializations = GameData.GetSpecialization(types.ToArray());
            GameSettings settings = GameSettings.Instance;
            settings.Specializations = specializations[0];
            settings.CodeSpecializations = specializations[1];
            settings.ArtSpecializations = specializations[2];
        }

        private static void AddGenerators(ModPlan plan, MarketSimulation simulation)
        {
            foreach (KeyValuePair<string, RandomNameGenerator> item in plan.NewGenerators)
            {
                if (!simulation.RNG.ContainsKey(item.Key))
                {
                    simulation.RNG[item.Key] = item.Value;
                }
            }
        }

        private static void AddTypes(ModPlan plan, MarketSimulation simulation)
        {
            uint next = 2u;
            foreach (SoftwareType type in simulation.SoftwareTypes.Values)
            {
                if (type.ID >= next)
                {
                    next = type.ID + 1u;
                }
            }

            for (int i = 0; i < plan.NewTypes.Count; i++)
            {
                SoftwareType type = plan.NewTypes[i];
                if (simulation.SoftwareTypes.ContainsKey(type.Name))
                {
                    continue;
                }
                type.UpdateID(next);
                next++;
                simulation.SoftwareTypes[type.Name] = type;
                foreach (SoftwareCategory category in type.Categories.Values)
                {
                    TrackOSSupport(simulation, category);
                }
            }
        }

        private static void AddCategories(ModPlan plan, MarketSimulation simulation)
        {
            for (int i = 0; i < plan.NewCategories.Count; i++)
            {
                CategoryAddition addition = plan.NewCategories[i];
                if (addition.Owner.Categories.ContainsKey(addition.Category.Name))
                {
                    continue;
                }
                uint highest = 0u;
                foreach (SoftwareCategory category in addition.Owner.Categories.Values)
                {
                    if (category.ID > highest)
                    {
                        highest = category.ID;
                    }
                }
                // The game takes a category's type from Parent, so the category must point at the save's type, not at the clone.
                SoftwareCategory added = new SoftwareCategory(addition.Owner, addition.Category, null, null, null, null);
                if (added.SubmarketEq != null)
                {
                    added.SubmarketEq = (double[])added.SubmarketEq.Clone();
                }
                added.ID = highest + 1u;
                addition.Owner.Categories[addition.Category.Name] = added;
                TrackOSSupport(simulation, added);
            }
        }

        private static void AddFeatures(ModPlan plan)
        {
            for (int i = 0; i < plan.NewFeatures.Count; i++)
            {
                FeatureAddition addition = plan.NewFeatures[i];
                if (addition.Owner.Features.ContainsKey(addition.Feature.Name))
                {
                    continue;
                }
                uint highest = 0u;
                foreach (FeatureBase feature in addition.Owner.Features.Values)
                {
                    if (feature.ID > highest)
                    {
                        highest = feature.ID;
                    }
                }
                addition.Feature.ID = highest + 1u;
                addition.Owner.Features[addition.Feature.Name] = addition.Feature;
            }
        }

        private static void AddAddOns(ModPlan plan)
        {
            for (int i = 0; i < plan.NewAddOns.Count; i++)
            {
                AddOnAddition addition = plan.NewAddOns[i];
                if (addition.Owner.AddOns.ContainsKey(addition.AddOn.Name))
                {
                    continue;
                }
                uint highest = 0u;
                foreach (SoftwareAddOn addOn in addition.Owner.AddOns.Values)
                {
                    if (addOn.ID > highest)
                    {
                        highest = addOn.ID;
                    }
                }
                addition.AddOn.ID = highest + 1u;
                addition.AddOn.Parent = addition.Owner;
                uint next = 2u;
                foreach (AddOnFeature feature in addition.AddOn.Features.Values)
                {
                    feature.ID = next;
                    next++;
                }
                addition.Owner.AddOns[addition.AddOn.Name] = addition.AddOn;
            }
        }

        private static void UpdateTypes(ModPlan plan)
        {
            for (int i = 0; i < plan.TypeChanges.Count; i++)
            {
                string[] current = plan.TypeChanges[i].Current.SubMarkets;
                string[] target = plan.TypeChanges[i].Target.SubMarkets;
                for (int j = 0; j < current.Length && j < target.Length; j++)
                {
                    current[j] = target[j];
                }
            }
        }

        private static void UpdateDependencies(ModPlan plan)
        {
            for (int i = 0; i < plan.DependencyChanges.Count; i++)
            {
                string[] current = plan.DependencyChanges[i].Current.Dependencies;
                string[] target = plan.DependencyChanges[i].Target.Dependencies;
                for (int j = 0; j < current.Length && j < target.Length; j++)
                {
                    current[j] = target[j];
                }
            }
        }

        private static void AddAddOnChances(ModPlan plan)
        {
            for (int i = 0; i < plan.NewAddOnChances.Count; i++)
            {
                ChanceAddition addition = plan.NewAddOnChances[i];
                if (!addition.Owner.Addons.ContainsKey(addition.Key))
                {
                    addition.Owner.Addons[addition.Key] = addition.Chance;
                }
            }
        }

        private static void UpdateCategories(ModPlan plan, MarketSimulation simulation)
        {
            for (int i = 0; i < plan.CategoryChanges.Count; i++)
            {
                SoftwareCategory current = plan.CategoryChanges[i].Current;
                SoftwareCategory target = plan.CategoryChanges[i].Target;
                bool wasHidden = current.Hidden;
                current.Popularity = target.Popularity;
                current.Unlock = target.Unlock;
                current.LagBehind = target.LagBehind;
                current.Hidden = target.Hidden;
                CopyNumbers(target.SubmarketEq, current.SubmarketEq);
                if (wasHidden)
                {
                    TrackOSSupport(simulation, current);
                }
            }
        }

        private static void UpdateFeatures(ModPlan plan)
        {
            for (int i = 0; i < plan.FeatureChanges.Count; i++)
            {
                FeatureBase current = plan.FeatureChanges[i].Current;
                FeatureBase target = plan.FeatureChanges[i].Target;
                CopyNumbers(target.Submarkets, current.Submarkets);
                if (target.SoftwareCategories != null && current.SoftwareCategories != null)
                {
                    foreach (KeyValuePair<string, int> item in target.SoftwareCategories)
                    {
                        current.SoftwareCategories[item.Key] = item.Value;
                    }
                }
            }
        }

        private static void CopyNumbers(double[] source, double[] destination)
        {
            if (source == null || destination == null || source.Length != destination.Length)
            {
                return;
            }
            for (int i = 0; i < source.Length; i++)
            {
                destination[i] = source[i];
            }
        }

        private static void AddCompanyTypes(ModPlan plan, MarketSimulation simulation)
        {
            for (int i = 0; i < plan.NewCompanyTypes.Count; i++)
            {
                CompanyType type = plan.NewCompanyTypes[i];
                if (!simulation.CompanyTypes.ContainsKey(type.Name))
                {
                    simulation.CompanyTypes[type.Name] = type;
                }
            }
        }

        private static void AddChances(ModPlan plan)
        {
            for (int i = 0; i < plan.NewChances.Count; i++)
            {
                ChanceAddition addition = plan.NewChances[i];
                if (!addition.Owner.Types.ContainsKey(addition.Key))
                {
                    addition.Owner.Types[addition.Key] = addition.Chance;
                }
            }
        }

        private static void UpdateCompanies(ModPlan plan, MarketSimulation simulation)
        {
            for (int i = 0; i < plan.CompanyTargets.Count; i++)
            {
                CompanyType target = plan.CompanyTargets[i];
                Dictionary<string, string[]> wanted = target.GetTypes();
                foreach (SimulatedCompany company in simulation.Companies.Values)
                {
                    if (company.Type.Name == target.Name)
                    {
                        AddMissingPairs(company, wanted);
                    }
                }
            }
        }

        private static void AddMissingPairs(SimulatedCompany company, Dictionary<string, string[]> wanted)
        {
            foreach (KeyValuePair<string, string[]> item in wanted)
            {
                if (HasPair(company, item.Key, null))
                {
                    continue;
                }
                for (int i = 0; i < item.Value.Length; i++)
                {
                    if (!HasPair(company, item.Key, item.Value[i]))
                    {
                        company.Categories.Add(new KeyValuePair<string, string>(item.Key, item.Value[i]));
                    }
                }
            }
        }

        private static bool HasPair(SimulatedCompany company, string software, string category)
        {
            for (int i = 0; i < company.Categories.Count; i++)
            {
                KeyValuePair<string, string> pair = company.Categories[i];
                if (pair.Key == software && pair.Value == category)
                {
                    return true;
                }
            }
            return false;
        }

        private static void TrackOSSupport(MarketSimulation simulation, SoftwareCategory category)
        {
            // The game tracks software waiting for an OS. Categories of the OS type itself have no entry.
            bool operatingSystem = category.Parent != null && category.Parent.Name == "Operating System";
            if (!operatingSystem && !category.Hidden && !simulation.MissingOSSupport.ContainsKey(category))
            {
                simulation.MissingOSSupport[category] = new SHashSet<SoftwareProduct>();
            }
        }
    }
}
