﻿using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CraftingFilter
{
    [BepInPlugin("aedenthorn.CraftingFilter", "Crafting Filter", "0.6.1")]
    public class BepInExPlugin : BaseUnityPlugin
    {

        public static ConfigEntry<bool> modEnabled;
        public static ConfigEntry<bool> isDebug;
        public static ConfigEntry<int> nexusID;

        public static ConfigEntry<bool> useScrollWheel;
        public static ConfigEntry<bool> showMenu;
        public static ConfigEntry<string> scrollModKey;
        public static ConfigEntry<string> prevHotKey;
        public static ConfigEntry<string> nextHotKey;
        
        public static ConfigEntry<string> categoryFile;
        
        private static BepInExPlugin context;



        private static Dictionary<string, List<ItemDrop.ItemData.ItemType>> categoryDict = new Dictionary<string, List<ItemDrop.ItemData.ItemType>>();
        private static List<string> categoryNames = new List<string>();
        private static List<GameObject> dropDownList = new List<GameObject>();

        private static int lastCategoryIndex = 0;
        private Vector3 lastMousePos;
        private static bool isShowing = false;
        private static string craftText = "Craft";
        private static string assetPath;
        private static int tabCraftPressed = 0;

        public static void Dbgl(string str = "", bool pref = true)
        {
            if (isDebug.Value)
                Debug.Log((pref ? typeof(BepInExPlugin).Namespace + " " : "") + str);
        }
        private void Awake()
        {
            
            context = this;
            modEnabled = Config.Bind<bool>("General", "Enabled", true, "Enable this mod");
            isDebug = Config.Bind<bool>("General", "IsDebug", true, "Enable debug logs");
            nexusID = Config.Bind<int>("General", "NexusID", 1219, "Nexus mod ID for updates");
            nexusID.Value = 1219;

            useScrollWheel = Config.Bind<bool>("Settings", "ScrollWheel", true, "Use scroll wheel to switch filter");
            showMenu = Config.Bind<bool>("Settings", "ShowMenu", true, "Show filter menu on hover");
            scrollModKey = Config.Bind<string>("Settings", "ScrollModKey", "", "Modifer key to allow scroll wheel change. Use https://docs.unity3d.com/Manual/class-InputManager.html");
            
            categoryFile = Config.Bind<string>("Settings", "CategoryFile", "categories.json", "Category file name");
            
            prevHotKey = Config.Bind<string>("Settings", "HotKeyPrev", "", "Hotkey to switch to previous filter. Use https://docs.unity3d.com/Manual/class-InputManager.html");
            nextHotKey = Config.Bind<string>("Settings", "HotKeyNext", "", "Hotkey to switch to next filter. Use https://docs.unity3d.com/Manual/class-InputManager.html");

            assetPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), typeof(BepInExPlugin).Namespace);

            LoadCategories();

            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), null);
        }

        private void LoadCategories()
        {
            if (!Directory.Exists(assetPath))
            {
                Dbgl("Creating mod folder");
                Directory.CreateDirectory(assetPath);
            }

            string file = Path.Combine(assetPath, categoryFile.Value);
            CategoryData data;
            if (!File.Exists(file))
            {
                Dbgl("Creating category file");
                data = new CategoryData();
                File.WriteAllText(file, JsonUtility.ToJson(data));

            }
            else
            {
                data = JsonUtility.FromJson<CategoryData>(File.ReadAllText(file));
            }
            Dbgl("Loaded" + data.categories.Count + " categories");

            categoryDict.Clear();
            categoryNames.Clear();

            foreach (string cat in data.categories)
            {
                if (!cat.Contains(":"))
                    continue;
                string[] parts = cat.Split(':');
                string[] types = parts[1].Split(',');
                categoryNames.Add(parts[0]);
                
                categoryDict[parts[0]] = new List<ItemDrop.ItemData.ItemType>();
                foreach(string type in types)
                {
                    if (Enum.TryParse(type, out ItemDrop.ItemData.ItemType result))
                    {
                        categoryDict[parts[0]].Add(result);
                    }
                }
            }

            categoryNames.Sort(delegate (string a, string b)
            {
                if (categoryDict[a].Contains(ItemDrop.ItemData.ItemType.None))
                    return -1;
                if (categoryDict[b].Contains(ItemDrop.ItemData.ItemType.None))
                    return 1;
                return (a).CompareTo(b);
            });

        }


        private void Update()
        {
            if (!modEnabled.Value || !Player.m_localPlayer || !InventoryGui.IsVisible() || (!Player.m_localPlayer.GetCurrentCraftingStation() && !Player.m_localPlayer.NoCostCheat()))
            {
                lastCategoryIndex = 0;
                UpdateDropDown(false);
                return;
            }
            if (!InventoryGui.instance.InCraftTab())
            {
                UpdateDropDown(false);
                return;
            }

            bool hover = false;

            Vector3 mousePos = Input.mousePosition;

            if (lastMousePos == Vector3.zero)
                lastMousePos = mousePos;

            PointerEventData eventData = new PointerEventData(EventSystem.current)
            {
                position = lastMousePos
            };

            List<RaycastResult> raycastResults = new List<RaycastResult>();
            EventSystem.current.RaycastAll(eventData, raycastResults);
            foreach (RaycastResult rcr in raycastResults)
            {
                if (rcr.gameObject.layer == LayerMask.NameToLayer("UI"))
                {

                    if (rcr.gameObject.name == "Craft")
                    {
                        hover = true;
                        if (tabCraftPressed == 0)
                        {
                            if (useScrollWheel.Value && AedenthornUtils.CheckKeyHeld(scrollModKey.Value, false) && Input.mouseScrollDelta.y != 0)
                            {
                                SwitchFilter(Input.mouseScrollDelta.y < 0);
                            }
                            if (showMenu.Value)
                            {
                                UpdateDropDown(true);
                            }
                        }
                    }
                    else if(dropDownList.Contains(rcr.gameObject))
                    {
                        hover = true;
                    }

                }
            }

            if (!hover)
            {
                if(tabCraftPressed > 0)
                    tabCraftPressed--;
                UpdateDropDown(false);
            }

            if (AedenthornUtils.CheckKeyDown(prevHotKey.Value))
            {
                SwitchFilter(false);
            }
            else if(AedenthornUtils.CheckKeyDown(nextHotKey.Value))
            {
                SwitchFilter(true);
            }

            lastMousePos = Input.mousePosition;
        }

        private static void SwitchFilter(int idx)
        {
            //Dbgl($"switching to filter {idx}");

            lastCategoryIndex = idx;
            UpdateDropDown(false);
            SwitchFilter();
        }

        private static void SwitchFilter(bool next)
        {
            //Dbgl($"switching to {(next ? "next" : "last")} filter");

            if (next)
            {
                lastCategoryIndex++;
                lastCategoryIndex %= categoryNames.Count;
            }
            else
            {
                lastCategoryIndex--;
                if (lastCategoryIndex < 0)
                    lastCategoryIndex = categoryNames.Count - 1;
            }
            List<Recipe> recipes = new List<Recipe>();
            Player.m_localPlayer.GetAvailableRecipes(ref recipes);
            int count = 0;
            while (!categoryDict[categoryNames[lastCategoryIndex]].Contains(ItemDrop.ItemData.ItemType.None) && recipes.FindAll(r => categoryDict[categoryNames[lastCategoryIndex]].Contains(r.m_item.m_itemData.m_shared.m_itemType)).Count == 0 && count < categoryNames.Count)
            {
                count++;
                if (next)
                {
                    lastCategoryIndex++;
                    lastCategoryIndex %= categoryNames.Count;
                }
                else
                {
                    lastCategoryIndex--;
                    if (lastCategoryIndex < 0)
                        lastCategoryIndex = categoryNames.Count - 1;
                }
            }

            SwitchFilter();
        }

        private static void SwitchFilter()
        {
            List<Recipe> recipes = new List<Recipe>();
            Player.m_localPlayer.GetAvailableRecipes(ref recipes);
            Dbgl($"Switching to filter {categoryNames[lastCategoryIndex]} {recipes.Count} total recipes ");
            Traverse t = Traverse.Create(InventoryGui.instance);
            t.Method("UpdateRecipeList", new object[] { recipes }).GetValue();
            t.Method("SetRecipe", new object[] { 0, true }).GetValue();
            InventoryGui.instance.m_tabCraft.gameObject.GetComponentInChildren<Text>().text = craftText + (categoryDict[categoryNames[lastCategoryIndex]].Contains(ItemDrop.ItemData.ItemType.None) ? "" : "\n" + categoryNames[lastCategoryIndex]);
        }

        private static void GetFilteredRecipes(ref List<Recipe> recipes)
        {
            if(InventoryGui.instance.InCraftTab() && !categoryDict[categoryNames[lastCategoryIndex]].Contains(ItemDrop.ItemData.ItemType.None))
            {
                recipes = recipes.FindAll(r => categoryDict[categoryNames[lastCategoryIndex]].Contains(r.m_item.m_itemData.m_shared.m_itemType)); 
                Dbgl($"using filter {categoryNames[lastCategoryIndex]}, {recipes.Count} filtered recipes");
            }
        }


        private static void UpdateDropDown(bool show)
        {
            if (show == isShowing)
                return;
            if (show)
            {
                List<Recipe> recipes = new List<Recipe>();
                Player.m_localPlayer.GetAvailableRecipes(ref recipes);

                float gameScale = GameObject.Find("LoadingGUI").GetComponent<CanvasScaler>().scaleFactor;
                Vector2 pos = InventoryGui.instance.m_tabCraft.gameObject.transform.GetComponent<RectTransform>().position;
                float height = InventoryGui.instance.m_tabCraft.gameObject.transform.GetComponent<RectTransform>().rect.height * gameScale;

                int showCount = 0;
                for (int i = 0; i < categoryNames.Count; i++)
                {
                    int count = recipes.FindAll(r => categoryDict[categoryNames[i]].Contains(r.m_item.m_itemData.m_shared.m_itemType)).Count;
                    dropDownList[i].SetActive(count > 0 || categoryDict[categoryNames[i]].Contains(ItemDrop.ItemData.ItemType.None));
                    if (count > 0 || categoryDict[categoryNames[i]].Contains(ItemDrop.ItemData.ItemType.None))
                    {
                        dropDownList[i].GetComponent<RectTransform>().position = pos - new Vector2(0, height * (showCount++ + 1));
                        dropDownList[i].GetComponentInChildren<Text>().text = categoryNames[i] + (count == 0 ? "" : $" ({count})");
                    }
                }
            }
            else
            {
                for (int i = 0; i < categoryNames.Count; i++)
                {
                    dropDownList[i].SetActive(false);
                }

            }
            isShowing = show;
        }

        [HarmonyPatch(typeof(InventoryGui), "UpdateRecipeList")]
        static class UpdateRecipeList_Patch
        {

            static void Prefix(ref List<Recipe> recipes)
            {
                if (!modEnabled.Value || !Player.m_localPlayer.GetCurrentCraftingStation())
                    return;

                Dbgl($"updating recipes");

                GetFilteredRecipes(ref recipes);
            }
        }

        [HarmonyPatch(typeof(InventoryGui), "Hide")]
        static class Hide_Patch
        {

            static void Prefix()
            {
                if (!modEnabled.Value)
                    return;

                InventoryGui.instance.m_tabCraft.gameObject.GetComponentInChildren<Text>().text = craftText;
                lastCategoryIndex = 0;
            }
        }

        [HarmonyPatch(typeof(InventoryGui), "OnTabCraftPressed")]
        static class OnTabCraftPressed_Patch
        {

            static void Prefix()
            {
                if (!modEnabled.Value)
                    return;
                Dbgl("Tab craft pressed");
                tabCraftPressed = 2;
            }
        }
        
        [HarmonyPatch(typeof(InventoryGui), "Awake")]
        static class InventoryGui_Awake_Patch
        {
            static void Postfix(InventoryGui __instance)
            {
                if (!modEnabled.Value)
                    return;

                dropDownList.Clear();

                //buttonObj.transform.parent.SetAsLastSibling();
                craftText = __instance.m_tabCraft.gameObject.GetComponentInChildren<Text>().text;
                for (int i = 0; i < categoryNames.Count; i++)
                {
                    int idx = i;
                    GameObject go = Instantiate(__instance.m_tabCraft.gameObject);
                    go.name = categoryNames[i];
                    go.transform.SetParent(__instance.m_tabCraft.gameObject.transform.parent.parent);
                    go.GetComponent<Button>().interactable = true;
                    go.GetComponent<Button>().onClick = new Button.ButtonClickedEvent();
                    go.GetComponent<Button>().onClick.AddListener(() => SwitchFilter(idx));
                    go.SetActive(false);
                    dropDownList.Add(go);
                }
            }
        }


        [HarmonyPatch(typeof(Terminal), "InputText")]
        static class InputText_Patch
        {
            static bool Prefix(Terminal __instance)
            {
                if (!modEnabled.Value)
                    return true;
                string text = __instance.m_input.text;
                if (text.ToLower().Equals($"{typeof(BepInExPlugin).Namespace.ToLower()} reset"))
                {
                    context.Config.Reload();
                    context.Config.Save();

                    __instance.AddString(text);
                    __instance.AddString($"{context.Info.Metadata.Name} config reloaded");
                    return false;
                }
                return true;
            }
        }
    }
}
