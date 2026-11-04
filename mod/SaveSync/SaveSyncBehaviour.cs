using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace SaveSync
{
    public class SaveSyncBehaviour : ModBehaviour
    {
        private const float MinimumWait = 1f;
        private const float MaximumWait = 30f;
        private const int ShownLines = 12;
        private const int MaximumSources = 3;

        // Detailed market and plan logging used while testing. Off in released builds.
        private static bool Verbose = false;

        private Queue<ModPlan> _queue;
        private List<string> _accepted;
        private List<string> _skippedTitles;
        private List<ModPlan> _acceptedPlans;
        private Queue<TypeConflict> _conflicts;
        private TypeConflict _shownConflict;
        private Dictionary<string, Component> _conflictElements;
        private List<string> _choices;
        private List<string> _resolutions;
        private bool _sameForAll;
        private ModPlan _shown;
        private GameObject _window;
        private SaveGame _waitingFor;
        private float _waited;
        private bool _manual;
        private MarketSimulation _market;
        private List<string> _watchedCompanies;
        private List<string> _watchedSoftware;

        public override void OnActivate()
        {
            _watchedCompanies = new List<string>();
            _watchedSoftware = new List<string>();
            GameSettings.GameReady -= OnGameReady;
            GameSettings.GameReady += OnGameReady;
        }

        public override void OnDeactivate()
        {
            GameSettings.GameReady -= OnGameReady;
            StopWatching();
        }

        private void StopWatching()
        {
            if (_market != null)
            {
                _market.OnCompanyFounded -= OnCompanyFounded;
                _market.OnCompanyClosed -= OnCompanyClosed;
                _market.OnProductReleased -= OnProductReleased;
                _market = null;
            }
        }

        private void StartWatching(MarketSimulation simulation)
        {
            StopWatching();
            _market = simulation;
            _market.OnCompanyFounded += OnCompanyFounded;
            _market.OnCompanyClosed += OnCompanyClosed;
            _market.OnProductReleased += OnProductReleased;
        }

        private void OnCompanyFounded(object sender, Company company)
        {
            LogWatched("founded", company);
        }

        private void OnCompanyClosed(object sender, Company company)
        {
            LogWatched("closed", company);
        }

        private void LogWatched(string what, Company company)
        {
            SimulatedCompany simulated = company as SimulatedCompany;
            if (simulated == null || simulated.Type == null || !_watchedCompanies.Contains(simulated.Type.Name))
            {
                return;
            }
            StringBuilder line = new StringBuilder();
            line.Append("Save Sync: ");
            line.Append(what);
            line.Append(" ");
            line.Append(simulated.Name);
            line.Append(" type ");
            line.Append(simulated.Type.Name);
            line.Append(" date ");
            line.Append(TimeOfDay.Instance.Year);
            line.Append("-");
            line.Append(TimeOfDay.Instance.Month + 1);
            line.Append(" money ");
            line.Append(simulated.Money);
            line.Append(" bankrupt ");
            line.Append(simulated.Bankrupt);
            line.Append(" lead ");
            line.Append(simulated.LeadDesigner != null);
            line.Append(" products ");
            line.Append(simulated.Products.Count);
            line.Append(" categories");
            for (int i = 0; i < simulated.Categories.Count; i++)
            {
                line.Append(" [");
                line.Append(simulated.Categories[i].Key);
                line.Append(" / ");
                line.Append(simulated.Categories[i].Value);
                line.Append("]");
            }
            Debug.Log(line.ToString());
        }

        private void OnProductReleased(object sender, SoftwareProduct product)
        {
            if (product == null || product.Type == null || !_watchedSoftware.Contains(product.Type.Name))
            {
                return;
            }
            string category = product.Category != null ? product.Category.Name : "none";
            string maker = product.DevCompany != null ? product.DevCompany.Name : "none";
            Debug.Log("Save Sync: released " + product.Name + " type " + product.Type.Name + " / " + category + " by " + maker + " date " + TimeOfDay.Instance.Year + "-" + (TimeOfDay.Instance.Month + 1));
        }

        public override void Serialize(WriteDictionary data, GameReader.LoadMode mode)
        {
            if (mode == GameReader.LoadMode.Full)
            {
                EnsureState();
                SyncState.Write(data);
            }
        }

        public override void Deserialize(WriteDictionary data, GameReader.LoadMode mode)
        {
            if (mode != GameReader.LoadMode.Full)
            {
                return;
            }
            try
            {
                SyncState.Read(data);
                SyncState.LoadedFromRecord = true;
                if (SyncState.Pending)
                {
                    ApplyPending();
                }
            }
            catch (Exception exception)
            {
                SyncState.Failure = exception.Message;
                Debug.LogError("Save Sync: " + exception);
            }
        }

        private void Update()
        {
            if (_waitingFor == null)
            {
                return;
            }
            _waited += Time.unscaledDeltaTime;
            if (_waited >= MinimumWait && _waitingFor.FileSize > 0f)
            {
                SaveGame game = _waitingFor;
                _waitingFor = null;
                SaveGameManager.LoadGame(game, null, default(SDateTime), false, true, true, false);
            }
            else if (_waited >= MaximumWait)
            {
                _waitingFor = null;
                Show(Texts.L(
                    "The new save was not ready in time. Load it from the save list: the changes are applied when it loads.",
                    "Новый сейв не успел записаться. Загрузи его из списка сейвов: изменения применятся при загрузке."));
            }
        }

        private void OnGameReady(object sender, EventArgs arguments)
        {
            EnsureState();
            if (!SyncState.LoadedFromRecord)
            {
                SyncState.Reset();
            }
            SyncState.LoadedFromRecord = false;

            if (SyncState.Failure != null)
            {
                Show(Texts.L("Save Sync could not finish. Load your original save. Details: ", "Save Sync не смог закончить. Загрузи исходный сейв. Подробности: ") + SyncState.Failure);
                return;
            }
            // Right after a merge the player has just answered every window, so this load only reports.
            if (SyncState.JustApplied.Count > 0)
            {
                string message = Texts.L("Added: ", "Добавлено: ") + string.Join(", ", SyncState.JustApplied.ToArray());
                SyncState.JustApplied = new List<string>();
                int waiting = Allowed() ? Scan(false, true) : 0;
                if (waiting > 0)
                {
                    message += Environment.NewLine + Environment.NewLine + Texts.L("Mods still waiting for a decision: ", "Модов ждут решения: ") + waiting
                        + Texts.L(". They will be offered the next time you load this save. Skip a mod to stop being asked about it.", ". Их предложат при следующей загрузке этого сейва. Пропусти мод, чтобы он больше не спрашивал.");
                }
                Show(message);
                return;
            }
            if (Allowed())
            {
                Scan(false, false);
            }
        }

        private static void EnsureState()
        {
            if (SyncState.PendingMods == null)
            {
                SyncState.Reset();
            }
        }

        private static bool Allowed()
        {
            GameSettings settings = GameSettings.Instance;
            return settings != null
                && !settings.EditMode
                && !settings.IsNetworkMode
                && MarketSimulation.Active != null
                && (SaveGameManager.CurrentMode & GameReader.NewLoadMode.Full) != 0;
        }

        public void CheckAllMods()
        {
            if (Allowed())
            {
                Scan(true, false);
            }
            else
            {
                Show(Texts.L("Save Sync works only in a loaded single player game.", "Save Sync работает только в загруженной одиночной игре."));
            }
        }

        // Returns how many mods have something to offer. Quiet builds the plans and logs them without opening windows.
        private int Scan(bool manual, bool quiet)
        {
            _manual = manual;
            _queue = new Queue<ModPlan>();
            _accepted = new List<string>();
            _skippedTitles = new List<string>();
            _acceptedPlans = new List<ModPlan>();
            MarketSimulation simulation = MarketSimulation.Active;
            List<ModPackage> usedPackages = Planner.UsedPackages(simulation);
            List<string> shared = Planner.SharedTypes(usedPackages);
            List<ModPlan> plans = new List<ModPlan>();
            _watchedCompanies = new List<string>();
            _watchedSoftware = new List<string>();
            if (Verbose)
            {
                if (TimeOfDay.Instance != null)
                {
                    Debug.Log("Save Sync: game date " + TimeOfDay.Instance.Year + "-" + (TimeOfDay.Instance.Month + 1));
                }
                StartWatching(simulation);
                LogCategories("3D Editor", simulation);
                LogCategories("Operating System", simulation);
            }

            for (int i = 0; i < GameData.ModPackages.Count; i++)
            {
                ModPackage package = GameData.ModPackages[i];
                bool used = usedPackages.Contains(package);
                if (!used && !manual)
                {
                    continue;
                }
                if (used && Verbose)
                {
                    LogCompanies(package, simulation);
                    LogProducts(package, simulation);
                    _watchedCompanies.AddRange(package.CompanyTypes.Keys);
                    _watchedSoftware.AddRange(package.SoftwareTypes.Keys);
                    foreach (string typeName in package.SoftwareTypes.Keys)
                    {
                        LogCategories(typeName, simulation);
                    }
                }

                ModPlan plan = Planner.Build(package, simulation, shared, manual ? new List<string>() : ResolvedChoices());
                if (!manual)
                {
                    DropResolvedConflicts(plan);
                }
                plan.NewToSave = !used;
                LogPlan(plan);
                bool worthShowing = plan.HasChanges() || plan.Refused != null || plan.Conflicts.Count > 0;
                if (worthShowing && (manual || !SyncState.Skipped.Contains(plan.Fingerprint())))
                {
                    plans.Add(plan);
                }
            }

            if (quiet)
            {
                return plans.Count;
            }
            WarnAboutOverlaps(plans);
            for (int i = 0; i < plans.Count; i++)
            {
                _queue.Enqueue(plans[i]);
            }
            if (manual && plans.Count == 0)
            {
                Show(Texts.L("No mod has anything new for this save.", "Для этого сейва ни в одном моде нет ничего нового."));
                return 0;
            }
            ShowNext();
            return plans.Count;
        }

        // Resolved holds "type|mod" for a type whose numbers follow that mod, or "type|" when the player kept the save's numbers.
        private static List<string> ResolvedChoices()
        {
            List<string> choices = new List<string>();
            for (int i = 0; i < SyncState.Resolved.Count; i++)
            {
                if (!SyncState.Resolved[i].EndsWith("|"))
                {
                    choices.Add(SyncState.Resolved[i]);
                }
            }
            return choices;
        }

        private static string ResolvedType(string entry)
        {
            return entry.Substring(0, entry.LastIndexOf('|'));
        }

        private static void DropResolvedConflicts(ModPlan plan)
        {
            for (int i = 0; i < SyncState.Resolved.Count; i++)
            {
                string type = ResolvedType(SyncState.Resolved[i]);
                if (plan.Conflicts.Remove(type))
                {
                    plan.NotApplied.Remove(type + Texts.L(": other mods change this type too, you will choose whose numbers to use", ": этот тип меняют и другие моды, ты выберешь, чьи числа брать"));
                }
            }
        }

        private static void WarnAboutOverlaps(List<ModPlan> plans)
        {
            for (int i = 0; i < plans.Count; i++)
            {
                for (int j = 0; j < plans.Count; j++)
                {
                    if (i == j)
                    {
                        continue;
                    }
                    for (int k = 0; k < plans[i].Touched.Count; k++)
                    {
                        string type = plans[i].Touched[k];
                        if (plans[j].Touched.Contains(type))
                        {
                            plans[i].Warnings.Add(type + Texts.L(" is also changed by ", " также меняет мод ") + plans[j].Title);
                        }
                    }
                }
            }
        }

        private static void LogCompanies(ModPackage package, MarketSimulation simulation)
        {
            foreach (string typeName in package.CompanyTypes.Keys)
            {
                if (!simulation.CompanyTypes.ContainsKey(typeName))
                {
                    continue;
                }
                StringBuilder line = new StringBuilder();
                line.Append("Save Sync: company type ");
                line.Append(typeName);
                line.Append(": ");
                int count = 0;
                foreach (SimulatedCompany company in simulation.Companies.Values)
                {
                    if (company.Type != null && company.Type.Name == typeName)
                    {
                        count++;
                        line.Append(company.Name);
                        line.Append(" (founded ");
                        line.Append(company.Founded.Year);
                        line.Append("-");
                        line.Append(company.Founded.Month + 1);
                        line.Append(", ");
                        line.Append(company.Products.Count);
                        line.Append(" products) ");
                    }
                }
                line.Append("total ");
                line.Append(count);
                Debug.Log(line.ToString());
            }
        }

        private static void LogCategories(string typeName, MarketSimulation simulation)
        {
            SoftwareType type;
            if (!simulation.SoftwareTypes.TryGetValue(typeName, out type))
            {
                return;
            }
            foreach (SoftwareCategory category in type.Categories.Values)
            {
                int known = 0;
                foreach (Dictionary<SoftwareCategory, int> levels in simulation.MarketTechLevels.Values)
                {
                    if (levels.ContainsKey(category))
                    {
                        known++;
                    }
                }
                string parent = "other";
                if (category.Parent == null)
                {
                    parent = "null";
                }
                else if (object.ReferenceEquals(category.Parent, type))
                {
                    parent = "same";
                }
                Debug.Log("Save Sync: category " + typeName + " / " + category.Name
                    + " typeId " + type.ID
                    + " id " + category.ID
                    + " parent " + parent
                    + " hidden " + category.Hidden
                    + " unlock " + category.Unlock
                    + " popularity " + category.Popularity
                    + " generator " + Planner.GeneratorKnown(category, simulation.RNG)
                    + " missingOS " + simulation.MissingOSSupport.ContainsKey(category)
                    + " techLevels " + known + "/" + simulation.MarketTechLevels.Count);
            }
        }

        private static void LogProducts(ModPackage package, MarketSimulation simulation)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>();
            foreach (SimulatedCompany company in simulation.Companies.Values)
            {
                for (int i = 0; i < company.Products.Count; i++)
                {
                    SoftwareProduct product = company.Products[i];
                    if (product.Type == null || product.Category == null || !package.SoftwareTypes.ContainsKey(product.Type.Name))
                    {
                        continue;
                    }
                    string key = product.Type.Name + " / " + product.Category.Name;
                    int count;
                    counts.TryGetValue(key, out count);
                    counts[key] = count + 1;
                }
            }
            foreach (KeyValuePair<string, int> item in counts)
            {
                Debug.Log("Save Sync: products " + item.Key + ": " + item.Value);
            }
        }

        private static void LogPlan(ModPlan plan)
        {
            if (!Verbose)
            {
                Debug.Log("Save Sync: plan for " + plan.Title + ": added " + plan.Added.Count + ", changed " + plan.Changed.Count
                    + ", not applied " + plan.NotApplied.Count + ", conflicts " + plan.Conflicts.Count + (plan.Refused != null ? ", refused: " + plan.Refused : ""));
                return;
            }
            StringBuilder text = new StringBuilder();
            text.Append("Save Sync: plan for ");
            text.AppendLine(plan.Title);
            if (plan.Refused != null)
            {
                text.Append("  refused: ");
                text.AppendLine(plan.Refused);
            }
            AppendLog(text, "added", plan.Added);
            AppendLog(text, "changed", plan.Changed);
            AppendLog(text, "not applied", plan.NotApplied);
            AppendLog(text, "warning", plan.Warnings);
            foreach (KeyValuePair<string, List<string>> item in plan.Conflicts)
            {
                AppendLog(text, "conflict " + item.Key, item.Value);
            }
            Debug.Log(text.ToString());
        }

        private static void AppendLog(StringBuilder text, string label, List<string> lines)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                text.Append("  ");
                text.Append(label);
                text.Append(": ");
                text.AppendLine(lines[i]);
            }
        }

        private void ShowNext()
        {
            CloseWindow();
            if (_queue.Count == 0)
            {
                if (_accepted.Count > 0)
                {
                    BuildConflicts();
                    ShowNextConflict();
                }
                return;
            }
            _shown = _queue.Dequeue();
            Dictionary<string, Component> elements = WindowManager.GenerateUI(ParentMod.LoadFullXMLFile("UI.txt"), null, this);
            ((Text)elements["body"]).text = Describe(_shown);
            _window = elements["window"].gameObject;
            SetCaption(elements, "apply", Texts.L("Add to a new save", "Добавить в новый сейв"));
            SetCaption(elements, "skip", _manual ? Texts.L("Next mod", "Следующий мод") : Texts.L("Skip this mod", "Пропустить этот мод"));
            SetCaption(elements, "later", Texts.L("Decide later", "Решить позже"));
            Component apply;
            if (_shown.Refused != null && elements.TryGetValue("apply", out apply))
            {
                apply.gameObject.SetActive(false);
            }
        }

        private void BuildConflicts()
        {
            _choices = new List<string>();
            _resolutions = new List<string>();
            _conflicts = new Queue<TypeConflict>();
            _sameForAll = false;
            Dictionary<string, TypeConflict> byType = new Dictionary<string, TypeConflict>();
            for (int i = 0; i < _acceptedPlans.Count; i++)
            {
                ModPlan plan = _acceptedPlans[i];
                foreach (KeyValuePair<string, List<string>> item in plan.Conflicts)
                {
                    TypeConflict conflict;
                    if (!byType.TryGetValue(item.Key, out conflict))
                    {
                        conflict = new TypeConflict(item.Key);
                        byType[item.Key] = conflict;
                        _conflicts.Enqueue(conflict);
                    }
                    conflict.Mods.Add(plan.Title);
                    conflict.Lines.Add(item.Value);
                }
            }
        }

        private void ShowNextConflict()
        {
            CloseWindow();
            if (_conflicts.Count == 0)
            {
                StartSync();
                return;
            }
            _shownConflict = _conflicts.Dequeue();
            _conflictElements = WindowManager.GenerateUI(ParentMod.LoadFullXMLFile("Conflict.txt"), null, this);
            ((Text)_conflictElements["body"]).text = DescribeConflict(_shownConflict);
            _window = _conflictElements["window"].gameObject;
            for (int i = 0; i < MaximumSources; i++)
            {
                Component button;
                if (!_conflictElements.TryGetValue("source" + i, out button))
                {
                    continue;
                }
                if (i < _shownConflict.Mods.Count)
                {
                    SetCaption(_conflictElements, "source" + i, Texts.L("Use ", "Взять из ") + _shownConflict.Mods[i]);
                }
                else
                {
                    button.gameObject.SetActive(false);
                }
            }
            SetCaption(_conflictElements, "keep", Texts.L("Keep the numbers from the save", "Оставить числа из сейва"));
            SetCaption(_conflictElements, "same", SameCaption());
        }

        private string SameCaption()
        {
            if (_sameForAll)
            {
                return Texts.L("Same choice for all remaining types: on", "Тот же выбор для всех остальных типов: да");
            }
            return Texts.L("Same choice for all remaining types: off", "Тот же выбор для всех остальных типов: нет");
        }

        public void OnConflict(string choice)
        {
            if (choice == "same")
            {
                _sameForAll = !_sameForAll;
                SetCaption(_conflictElements, "same", SameCaption());
                return;
            }

            string source = null;
            if (choice != "keep")
            {
                source = _shownConflict.Mods[int.Parse(choice)];
            }
            RecordChoice(_shownConflict, source);
            while (_sameForAll && _conflicts.Count > 0)
            {
                RecordChoice(_conflicts.Dequeue(), source);
            }
            ShowNextConflict();
        }

        private void RecordChoice(TypeConflict conflict, string source)
        {
            if (source != null && conflict.Mods.Contains(source))
            {
                _choices.Add(conflict.Type + "|" + source);
                _resolutions.Add(conflict.Type + "|" + source);
            }
            else
            {
                _resolutions.Add(conflict.Type + "|");
            }
        }

        private static string DescribeConflict(TypeConflict conflict)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine(Texts.L("Conflict: ", "Конфликт: ") + conflict.Type);
            text.AppendLine(Texts.L("This type is changed by several mods and the save does not record their order. Choose whose numbers to use.", "Этот тип меняют несколько модов, а порядок модов в сейве не хранится. Выбери, чьи числа брать."));
            text.AppendLine();
            for (int i = 0; i < conflict.Mods.Count; i++)
            {
                AppendGroup(text, conflict.Mods[i] + ":", conflict.Lines[i]);
            }
            return text.ToString();
        }

        private static void SetCaption(Dictionary<string, Component> elements, string id, string caption)
        {
            Component element;
            if (elements.TryGetValue(id, out element))
            {
                Text text = element.GetComponentInChildren<Text>();
                if (text != null)
                {
                    text.text = caption;
                }
            }
        }

        public void OnChoice(string choice)
        {
            if (choice == "apply")
            {
                _accepted.Add(_shown.Title);
                _acceptedPlans.Add(_shown);
            }
            else if (choice == "later")
            {
                // Only this mod waits. The rest of the queue is still shown, so every decision can be made before the new save.
            }
            else if (!_manual)
            {
                SyncState.Skipped.Add(_shown.Fingerprint());
                _skippedTitles.Add(_shown.Title);
            }
            ShowNext();
        }

        private void CloseWindow()
        {
            if (_window != null)
            {
                Destroy(_window);
                _window = null;
            }
        }

        private static string Describe(ModPlan plan)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine(plan.Title);
            if (plan.NewToSave)
            {
                text.AppendLine(Texts.L("This mod is new for this save.", "Этого мода в сейве ещё нет."));
            }
            text.AppendLine();
            if (plan.Refused != null)
            {
                text.AppendLine(Texts.L("This mod cannot be applied:", "Этот мод нельзя применить:"));
                text.AppendLine(plan.Refused);
                return text.ToString();
            }

            AppendGroup(text, Texts.L("Will be added:", "Будет добавлено:"), plan.Added);
            AppendGroup(text, Texts.L("Will be updated:", "Будет обновлено:"), plan.Changed);
            List<string> conflicts = new List<string>();
            foreach (KeyValuePair<string, List<string>> item in plan.Conflicts)
            {
                for (int i = 0; i < item.Value.Count; i++)
                {
                    conflicts.Add(item.Key + " / " + item.Value[i]);
                }
            }
            AppendGroup(text, Texts.L("You will choose whose numbers to use:", "Ты выберешь, чьи числа брать:"), conflicts);
            AppendGroup(text, Texts.L("Will not be applied:", "Не будет применено:"), plan.NotApplied);
            AppendGroup(text, Texts.L("Warnings:", "Предупреждения:"), plan.Warnings);
            text.AppendLine(Texts.L("Products and projects already started keep their old features.", "Уже начатые продукты и проекты сохранят старые фичи."));
            text.AppendLine(Texts.L("Your save is kept. The result goes to a new save file.", "Твой сейв не изменится. Результат пойдёт в новый файл."));
            return text.ToString();
        }

        private static void AppendGroup(StringBuilder text, string title, List<string> lines)
        {
            if (lines.Count == 0)
            {
                return;
            }
            text.AppendLine(title);
            int shown = Mathf.Min(lines.Count, ShownLines);
            for (int i = 0; i < shown; i++)
            {
                text.AppendLine(lines[i]);
            }
            if (lines.Count > shown)
            {
                text.AppendLine(Texts.L("and ", "и ещё ") + (lines.Count - shown) + Texts.L(" more", ""));
            }
            text.AppendLine();
        }

        private void StartSync()
        {
            // A conflict answered with the save's numbers stays a conflict, so the window would ask again on every load.
            for (int i = 0; i < _acceptedPlans.Count; i++)
            {
                if (!SyncState.Skipped.Contains(_acceptedPlans[i].Fingerprint()))
                {
                    SyncState.Skipped.Add(_acceptedPlans[i].Fingerprint());
                }
            }
            for (int i = 0; i < _resolutions.Count; i++)
            {
                string type = ResolvedType(_resolutions[i]);
                for (int j = SyncState.Resolved.Count - 1; j >= 0; j--)
                {
                    if (ResolvedType(SyncState.Resolved[j]) == type)
                    {
                        SyncState.Resolved.RemoveAt(j);
                    }
                }
                SyncState.Resolved.Add(_resolutions[i]);
            }
            SyncState.Pending = true;
            SyncState.PendingMods = new List<string>(_accepted);
            SyncState.PendingChoices = new List<string>(_choices);
            SyncState.PendingSkipped = new List<string>(_skippedTitles);
            SaveGame game = SaveGame.SaveCurrentGame(NewSaveName(), false, SaveGameManager.CurrentMode);
            SyncState.Pending = false;
            SyncState.PendingMods = new List<string>();
            SyncState.PendingChoices = new List<string>();
            SyncState.PendingSkipped = new List<string>();
            _accepted = new List<string>();
            _acceptedPlans = new List<ModPlan>();

            if (game == null)
            {
                Show(Texts.L("The new save could not be written. Nothing was changed.", "Не удалось записать новый сейв. Ничего не изменилось."));
                return;
            }
            _waitingFor = game;
            _waited = 0f;
        }

        private static string NewSaveName()
        {
            StringBuilder name = new StringBuilder();
            string company = GameSettings.Instance.MyCompany.Name;
            for (int i = 0; i < company.Length; i++)
            {
                if (char.IsLetterOrDigit(company[i]))
                {
                    name.Append(company[i]);
                }
            }
            name.Append(" synced ");
            name.Append(DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss"));
            return name.ToString();
        }

        private static void ApplyPending()
        {
            MarketSimulation simulation = MarketSimulation.Active;
            if (simulation == null)
            {
                throw new InvalidOperationException("The market is not loaded yet.");
            }

            List<string> shared = Planner.SharedTypes(Planner.UsedPackages(simulation));
            List<string> choices = ResolvedChoices();
            choices.AddRange(SyncState.PendingChoices);
            List<ModPlan> plans = new List<ModPlan>();
            for (int i = 0; i < GameData.ModPackages.Count; i++)
            {
                ModPackage package = GameData.ModPackages[i];
                if (!SyncState.PendingMods.Contains(package.ItemTitle))
                {
                    continue;
                }
                ModPlan plan = Planner.Build(package, simulation, shared, choices);
                if (plan.Refused != null)
                {
                    throw new InvalidOperationException(plan.Refused);
                }
                plans.Add(plan);
            }

            for (int i = 0; i < plans.Count; i++)
            {
                string fingerprint = plans[i].Fingerprint();
                Merger.Apply(plans[i], simulation);
                SyncState.Applied.Add(fingerprint);
                SyncState.JustApplied.Add(plans[i].Title);
            }
            Merger.RefreshSpecializations(simulation);
            RememberSkipped(simulation);
            SyncState.PendingSkipped = new List<string>();
            SyncState.Pending = false;
            SyncState.PendingMods = new List<string>();
            SyncState.PendingChoices = new List<string>();
        }

        // A skipped mod is remembered by the fingerprint of its changes, and the merge changes them:
        // conflicts the player resolved drop out. So skipped mods are fingerprinted again on the merged market.
        private static void RememberSkipped(MarketSimulation simulation)
        {
            if (SyncState.PendingSkipped.Count == 0)
            {
                return;
            }
            List<string> shared = Planner.SharedTypes(Planner.UsedPackages(simulation));
            for (int i = 0; i < GameData.ModPackages.Count; i++)
            {
                ModPackage package = GameData.ModPackages[i];
                if (!SyncState.PendingSkipped.Contains(package.ItemTitle))
                {
                    continue;
                }
                ModPlan plan = Planner.Build(package, simulation, shared, ResolvedChoices());
                DropResolvedConflicts(plan);
                if (!SyncState.Skipped.Contains(plan.Fingerprint()))
                {
                    SyncState.Skipped.Add(plan.Fingerprint());
                }
            }
        }

        private static void Show(string message)
        {
            WindowManager.Instance.ShowMessageBox(message, true, DialogWindow.DialogType.Information);
        }
    }
}
