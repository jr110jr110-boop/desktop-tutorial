using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApi.Filters;

namespace MsTests
{
    /// <summary>
    /// Every API action answers only to a signed-in user, except the few routes that are anonymous by design (login, logout, the API settings,
    /// the attendance device push with its own key). Every write action (POST, PUT, PATCH, DELETE) also names who may write: a role list or a
    /// policy on the action or the controller, a <see cref="RolesCheckedInServiceAttribute"/> on a controller whose services check the caller,
    /// or an entry with a one-line reason in the allow-list below (R1a).
    /// </summary>
    [TestClass]
    public class ControllerAuthorizationTests
    {
        private static readonly HashSet<string> AnonymousByDesign = new HashSet<string>(StringComparer.Ordinal)
        {
            "AccountsController.LoginAsync", "AccountsController.LogoutAsync", "AccountsController.RefreshTokenAsync",
            // R1a: the user who signed in with a temporary password has no token yet; the temporary password is the proof
            "AccountsController.ChangeTemporaryPasswordAsync",
            "ApiSettingsController.Get", "OfficeAttendanceController.PushAsync",
        };

        /// <summary>
        /// Write actions that are open to every signed-in user on purpose, and write actions that the review found open and that a later round must fix.
        /// Key: "Controller.Action". Value: the one-line reason. A reason that starts with "FINDING" is not a decision but an open finding.
        /// An entry on a controller that carries [RolesCheckedInService] is an exception to that marker (the marker does not hold for this action).
        /// </summary>
        private static readonly Dictionary<string, string> OpenWriteActions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // identity: self-service (the service compares the caller with the target user)
            { "AccountsController.ChangePasswordAsync", "own password only: the service refuses another user id and asks for the current password" },
            { "AccountsController.AttachProfilePhotoAsync", "own photo for everybody, any photo for the PMO: the service checks the caller" },
            { "AccountsController.DetachProfilePhotoAsync", "own photo for everybody, any photo for the PMO: the service checks the caller" },
            { "AccountsController.GetAllRolesAsync", "a read-only list of the role names (a POST for historical reasons); nothing is changed" },

            // the assistant acts as the caller
            { "AiChatController.ChatAsync", "self-service: the assistant acts as the caller, every tool re-checks its RequiredRoles and a conversation belongs to its user" },
            { "AiChatController.VoiceAsync", "self-service: the same chat by voice; every tool re-checks its RequiredRoles and a conversation belongs to its user" },

            // documents: no owner is stored (a schema change is needed), so an ownership rule cannot be written yet
            { "DocumentsController.UploadAsync", "FINDING: every module needs attachments, so any signed-in user may upload; the document has no uploader field" },
            { "DocumentsController.ChangeAsync", "FINDING: any signed-in user can replace the file of any document id; the document has no owner field (needs CreatedByUserId)" },
            { "DocumentsController.UpdateDocumentDescriptionAsync", "FINDING: any signed-in user can edit the description of any document id; the document has no owner field" },

            // exceptions to [RolesCheckedInService]: the service has no check for this action (or for one branch of it)
            { "OfficeLettersController.SavePartyAsync", "FINDING: LetterService.SavePartyAsync has no role or owner check; any signed-in user can add or change a correspondent" },
            { "ProcurementController.InvoiceActionAsync", "FINDING: PayablesService.InvoiceActionAsync has a role check for approve but none for cancel; any signed-in user can cancel an unapproved supplier invoice" },
        };

        private static bool NamesRolesOrPolicy(IEnumerable<AuthorizeAttribute> attributes) =>
            attributes.Any(a => !string.IsNullOrWhiteSpace(a.Roles) || !string.IsNullOrWhiteSpace(a.Policy));

        private static bool IsWriteAction(MethodInfo action) =>
            action.GetCustomAttributes<HttpPostAttribute>(inherit: true).Any() || action.GetCustomAttributes<HttpPutAttribute>(inherit: true).Any()
            || action.GetCustomAttributes<HttpDeleteAttribute>(inherit: true).Any() || action.GetCustomAttributes<HttpPatchAttribute>(inherit: true).Any();

        private static IEnumerable<Type> Controllers() =>
            typeof(WebApi.Controllers.BaseApiController).Assembly.GetTypes().Where(t => !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t));

        private static IEnumerable<MethodInfo> Actions(Type controller) =>
            controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any());

        [TestMethod]
        public void EveryActionIsProtectedOrAnonymousByDesign()
        {
            var unprotected = new List<string>();
            var anonymous = new List<string>();
            foreach (Type controller in Controllers())
            {
                bool classAuthorized = controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any();
                foreach (MethodInfo action in Actions(controller))
                {
                    string name = controller.Name + "." + action.Name;
                    bool anon = action.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any() || controller.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any();
                    bool authorized = classAuthorized || action.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any();
                    if (anon) anonymous.Add(name);
                    else if (!authorized) unprotected.Add(name);
                }
            }
            Assert.AreEqual(0, unprotected.Count, "actions without [Authorize]: " + string.Join(", ", unprotected));
            string[] unexpected = anonymous.Where(a => !AnonymousByDesign.Contains(a)).ToArray();
            Assert.AreEqual(0, unexpected.Length, "anonymous actions that are not on the allow-list: " + string.Join(", ", unexpected));
        }

        [TestMethod]
        public void EveryWriteActionHasRolesOrIsAllowListed()
        {
            var uncovered = new List<string>();
            var staleBecauseProtected = new List<string>();
            foreach (Type controller in Controllers())
            {
                bool classRoles = NamesRolesOrPolicy(controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true));
                bool marked = controller.GetCustomAttributes<RolesCheckedInServiceAttribute>(inherit: true).Any();
                foreach (MethodInfo action in Actions(controller).Where(IsWriteAction))
                {
                    string name = controller.Name + "." + action.Name;
                    if (AnonymousByDesign.Contains(name)) continue;   // proven by its own secret (login, refresh, device key): see the first test
                    bool actionRoles = NamesRolesOrPolicy(action.GetCustomAttributes<AuthorizeAttribute>(inherit: true));
                    bool listed = OpenWriteActions.ContainsKey(name);
                    if (listed && (actionRoles || classRoles)) staleBecauseProtected.Add(name);
                    if (!actionRoles && !classRoles && !marked && !listed) uncovered.Add(name);
                }
            }
            Assert.AreEqual(0, uncovered.Count,
                "write actions without a role list, a policy, [RolesCheckedInService] or an entry in the allow-list (" + uncovered.Count + "):\n" + string.Join("\n", uncovered.OrderBy(x => x)));
            Assert.AreEqual(0, staleBecauseProtected.Count, "allow-list entries that now have a role list or policy (remove them): " + string.Join(", ", staleBecauseProtected));
        }

        [TestMethod]
        public void AllowListHasNoStaleEntriesAndEveryEntryHasAReason()
        {
            var writeActions = new HashSet<string>(StringComparer.Ordinal);
            foreach (Type controller in Controllers())
                foreach (MethodInfo action in Actions(controller).Where(IsWriteAction))
                    writeActions.Add(controller.Name + "." + action.Name);

            string[] stale = OpenWriteActions.Keys.Where(k => !writeActions.Contains(k)).ToArray();
            Assert.AreEqual(0, stale.Length, "allow-list entries that match no write action (renamed or removed): " + string.Join(", ", stale));
            string[] noReason = OpenWriteActions.Where(kv => string.IsNullOrWhiteSpace(kv.Value) || kv.Value.Length < 12 || kv.Value.Contains('\n')).Select(kv => kv.Key).ToArray();
            Assert.AreEqual(0, noReason.Length, "allow-list entries without a one-line reason: " + string.Join(", ", noReason));
            string[] anonymousListed = OpenWriteActions.Keys.Where(AnonymousByDesign.Contains).ToArray();
            Assert.AreEqual(0, anonymousListed.Length, "anonymous-by-design actions belong to the first test, not to this list: " + string.Join(", ", anonymousListed));
        }

        [TestMethod]
        public void MarkerIsOnlyOnControllersWithoutOtherRoleChecks()
        {
            // a controller that carries the marker AND role lists on every write action would only hide a stale marker: say so
            var redundant = new List<string>();
            foreach (Type controller in Controllers().Where(c => c.GetCustomAttributes<RolesCheckedInServiceAttribute>(inherit: true).Any()))
            {
                if (NamesRolesOrPolicy(controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true)))
                { redundant.Add(controller.Name + " (role list on the class)"); continue; }
                var writes = Actions(controller).Where(IsWriteAction).ToList();
                if (writes.Count > 0 && writes.All(a => NamesRolesOrPolicy(a.GetCustomAttributes<AuthorizeAttribute>(inherit: true))))
                    redundant.Add(controller.Name + " (every write action has its own role list)");
            }
            Assert.AreEqual(0, redundant.Count, "controllers with [RolesCheckedInService] that do not need it: " + string.Join(", ", redundant));
        }
    }
}
