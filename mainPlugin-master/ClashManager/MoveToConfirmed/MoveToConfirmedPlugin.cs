using Autodesk.Navisworks.Api.Clash;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autodesk.Navisworks.Api;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;

namespace MoveToConfirmedPlugin
{
    public class MoveToConfirmed
    {
        private Document doc;
        private DocumentClash documentClash;
        private DocumentClashTests clashTests;

        public MoveToConfirmed()
        {
            doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
            documentClash = doc.GetClash();
            clashTests = documentClash.TestsData;
        }

        public void Execute()
        {
            SetReviewed();
        }

        public List<ClashResultGroup> GetAllClashResGroups()
        {
            List<ClashResultGroup> AllGroups = new List<ClashResultGroup>();
            foreach (ClashTest test in clashTests.Tests)
            {
                if (test.Children.Count <= 0)
                {
                    continue;
                }
                foreach (ClashResultGroup group in test.Children.OfType<ClashResultGroup>())
                {
                    if ((group.Status.ToString() == "Active" || group.Status.ToString() == "New") && group.AssignedTo.ToString() != "")
                    {
                        AllGroups.Add(group);
                    }
                }
            }
            return AllGroups;
        }

        public void SetReviewed()
        {
            List<ClashResultGroup> AllGroups = GetAllClashResGroups();
            foreach (ClashResultGroup group in AllGroups)
            {
                IClashResult iGroup = group as IClashResult;
                documentClash.TestsData.TestsEditResultStatus(group, ClashResultStatus.Approved);
            }
        }
    }
}