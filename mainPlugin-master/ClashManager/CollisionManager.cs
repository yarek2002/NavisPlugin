using ClashManager;
using Autodesk.Navisworks.Api.Clash;
using Autodesk.Navisworks.Api.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using ClashManager.Externals;
using CollisionGrouperPlugin.Views;

namespace ClashManager
{
    [PluginAttribute("VPERED123",            //Plugin name
                    "ADSK",                                       //4 character Developer ID or GUID
                    ToolTip = "BasicPlugIn.ABasicPlugin tool tip",//The tooltip for the item in the ribbon
                    DisplayName = "VPERED 233")]          //Display name for the Plugin in the Ribbon
    [RibbonLayout("AddinRibbon.xaml")]
    [RibbonTab("CUSTOM_TAB1", DisplayName = "ClashUnionManager")]
    [Command("CollisionFragmentGrouping", LargeIcon = "IMG/1_32.png", ToolTip = "группировка по фрагментам")]
    [Command("ClashMaker", LargeIcon = "IMG/2_32.png", ToolTip = "группировка по кластерам")]
    [Command("MoveToFake", LargeIcon = "IMG/3_32.png", ToolTip = "Перенести в проанализированно")]
    [Command("FindSearchSets", LargeIcon = "IMG/5_32.png", ToolTip = "Поиск наборов и правил Clash по элементам")] // Новая команда: укажите подходящую иконку
    [Command("MagicWand", LargeIcon = "IMG/4_32.png", ToolTip = "Автоматическая группировка и кластеризация")]
    [Command("ManagerCollision", LargeIcon = "IMG/6_32.png", ToolTip = "Менеджер")]
    [Command("AutoNaming", LargeIcon = "IMG/7_32.png", ToolTip = "Авто-наименование групп коллизий")]
    [Command("ZoneAssignment", LargeIcon = "IMG/6_32.png", ToolTip = "Назначение зон коллизиям")]
    [Command("ZoneGrouping", LargeIcon = "IMG/4_32.png", ToolTip = "Авто-группировка по зонам")]


    public class CollisionGrouperCommandHandler : CommandHandlerPlugin
    {
        private string _commandName;

        public override int ExecuteCommand(string name, params string[] parameters)
        {
            try
            {
                IExternalCommand command = null;
                this._commandName = name;
                switch (_commandName)
                {
                    case "CollisionFragmentGrouping":
                        command = (IExternalCommand)new CollisionGrouperCmd();
                        break;
                    case "ClashMaker":
                        command = (IExternalCommand)new MakeClusterCmd();
                        break;
                    case "MoveToFake":
                        command = (IExternalCommand)new MoveToConfirmedCmd();
                        break;
                    case "FindSearchSets":
                        command = (IExternalCommand)new FindSearchSetsCmd();
                        break;
                    case "MagicWand":
                        command = (IExternalCommand)new MagicWandCmd();
                        break;
                    case "ManagerCollision":
                        command = (IExternalCommand)new ManagerCollisionCmd();
                        break;
                    case "AutoNaming":
                        command = (IExternalCommand)new AutoNamingCmd();
                        break;
                    case "ZoneAssignment":
                        command = (IExternalCommand)new ZoneAssignmentCmd();
                        break;
                    case "ZoneGrouping":
                        command = (IExternalCommand)new ZoneGroupingCmd();
                        break;
                }
                command.Execute();
            }
            catch (Exception ex)
            {
                int num = (int)MessageBox.Show(ex.Message + "/n/n" + ex.StackTrace);
            }
            return 0;
        }


    }
}
