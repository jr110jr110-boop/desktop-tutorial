using System;

namespace WebApi.Filters
{
    /// <summary>
    /// R1a: marks a controller whose services decide who may write: every write action of the controller ends in a service method that
    /// checks the caller (<c>IsInRole</c> / a role helper; in the office module also the caller's organisation position and the owner of the
    /// record) and answers with a refusal. It tells the reflection test
    /// (<c>ControllerAuthorizationTests.EveryWriteActionHasRolesOrIsAllowListed</c>) that the missing <c>[Authorize(Roles = ...)]</c> is intended.
    ///
    /// Place it only after reading the services behind the controller: a controller with this attribute whose service forgets a check
    /// is open to every signed-in user and nothing else would notice. Write actions that really are open to everyone (self-service), and the
    /// actions of a marked controller for which the marker does not hold (an open finding), are listed one by one, with a reason, in the
    /// allow-list of that test.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class RolesCheckedInServiceAttribute : Attribute
    {
    }
}
