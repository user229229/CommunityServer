/*
 *
 * (c) Copyright Ascensio System Limited 2010-2020
 *
 * This program is freeware. You can redistribute it and/or modify it under the terms of the GNU 
 * General Public License (GPL) version 3 as published by the Free Software Foundation (https://www.gnu.org/copyleft/gpl.html). 
 * In accordance with Section 7(a) of the GNU GPL its Section 15 shall be amended to the effect that 
 * Ascensio System SIA expressly excludes the warranty of non-infringement of any third-party rights.
 *
 * THIS PROGRAM IS DISTRIBUTED WITHOUT ANY WARRANTY; WITHOUT EVEN THE IMPLIED WARRANTY OF MERCHANTABILITY OR
 * FITNESS FOR A PARTICULAR PURPOSE. For more details, see GNU GPL at https://www.gnu.org/copyleft/gpl.html
 *
 * You can contact Ascensio System SIA by email at sales@onlyoffice.com
 *
 * The interactive user interfaces in modified source and object code versions of ONLYOFFICE must display 
 * Appropriate Legal Notices, as required under Section 5 of the GNU GPL version 3.
 *
 * Pursuant to Section 7 § 3(b) of the GNU GPL you must retain the original ONLYOFFICE logo which contains 
 * relevant author attributions when distributing the software. If the display of the logo in its graphic 
 * form is not reasonably feasible for technical reasons, you must include the words "Powered by ONLYOFFICE" 
 * in every copy of the program you distribute. 
 * Pursuant to Section 7 § 3(e) we decline to grant you any rights under trademark law for use of our trademarks.
 *
*/


using System.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using ASC.ActiveDirectory.Base;
using ASC.ActiveDirectory.Base.Data;
using ASC.ActiveDirectory.Base.Expressions;
using ASC.ActiveDirectory.Base.Settings;

namespace ASC.ActiveDirectory.Novell
{
    public class NovellLdapHelper : LdapHelper
    {
        public NovellLdapSearcher LDAPSearcher { get; private set; }

        public NovellLdapHelper(LdapSettings settings) :
            base(settings)
        {
            var password = string.IsNullOrEmpty(settings.Password)
                ? GetPassword(settings.PasswordBytes)
                : settings.Password;

            LDAPSearcher = new NovellLdapSearcher(settings.Login, password, settings.Server, settings.PortNumber,
                settings.StartTls, settings.Ssl, settings.AcceptCertificate, settings.AcceptCertificateHash);
        }

        public override bool IsConnected
        {
            get { return LDAPSearcher.IsConnected; }
        }

        public override void Connect()
        {
            LDAPSearcher.Connect();

            Settings.AcceptCertificate = LDAPSearcher.AcceptCertificate;
            Settings.AcceptCertificateHash = LDAPSearcher.AcceptCertificateHash;
        }

        public override Dictionary<string, string[]> GetCapabilities()
        {
            return LDAPSearcher.GetCapabilities();
        }

        public override string SearchDomain()
        {
            try
            {
                var capabilities = GetCapabilities();

                if (capabilities.Any())
                {
                    if (capabilities.ContainsKey("defaultNamingContext"))
                    {
                        var dnList = capabilities["defaultNamingContext"];

                        var dn = dnList.FirstOrDefault(dc =>
                            !string.IsNullOrEmpty(dc) &&
                            dc.IndexOf("dc=", StringComparison.InvariantCultureIgnoreCase) != -1);

                        var domain = LdapUtils.DistinguishedNameToDomain(dn);

                        if (!string.IsNullOrEmpty(domain))
                            return domain;
                    }

                    if (capabilities.ContainsKey("rootDomainNamingContext"))
                    {
                        var dnList = capabilities["rootDomainNamingContext"];

                        var dn = dnList.FirstOrDefault(dc =>
                            !string.IsNullOrEmpty(dc) &&
                            dc.IndexOf("dc=", StringComparison.InvariantCultureIgnoreCase) != -1);

                        var domain = LdapUtils.DistinguishedNameToDomain(dn);

                        if (!string.IsNullOrEmpty(domain))
                            return domain;
                    }

                    if (capabilities.ContainsKey("namingContexts"))
                    {
                        var dnList = capabilities["namingContexts"];

                        var dn = dnList.FirstOrDefault(dc =>
                            !string.IsNullOrEmpty(dc) &&
                            dc.IndexOf("dc=", StringComparison.InvariantCultureIgnoreCase) != -1);

                        var domain = LdapUtils.DistinguishedNameToDomain(dn);

                        if (!string.IsNullOrEmpty(domain))
                            return domain;
                    }
                }
            }
            catch (Exception e)
            {
                Log.WarnFormat("NovellLdapHelper->SearchDomain() failed. Error: {0}", e);
            }

            try
            {
                var searchResult =
                    LDAPSearcher.Search(Settings.UserDN, NovellLdapSearcher.LdapScope.Sub, Settings.UserFilter, limit: 1)
                        .FirstOrDefault();

                return searchResult != null ? searchResult.GetDomainFromDn() : null;
            }
            catch (Exception e)
            {
                Log.WarnFormat("NovellLdapHelper->SearchDomain() failed. Error: {0}", e);
            }

            return null;
        }

        public override void CheckCredentials(string login, string password, string server, int portNumber,
            bool startTls, bool ssl, bool acceptCertificate, string acceptCertificateHash)
        {
            using (var novellLdapSearcher = new NovellLdapSearcher(login, password, server, portNumber,
                startTls, ssl, acceptCertificate, acceptCertificateHash))
            {
                novellLdapSearcher.Connect();
            }
        }

        public override bool CheckUserDn(string userDn)
        {
            string[] attributes = {LdapConstants.ADSchemaAttributes.OBJECT_CLASS};

            var searchResult = LDAPSearcher.Search(userDn, NovellLdapSearcher.LdapScope.Base,
                LdapConstants.OBJECT_FILTER, attributes, 1);

            if (searchResult.Any())
                return true;

            Log.ErrorFormat("NovellLdapHelper->CheckUserDn(userDn: {0}) Wrong User DN parameter", userDn);
            return false;
        }

        public override bool CheckGroupDn(string groupDn)
        {
            string[] attributes = {LdapConstants.ADSchemaAttributes.OBJECT_CLASS};

            var searchResult = LDAPSearcher.Search(groupDn, NovellLdapSearcher.LdapScope.Base,
                LdapConstants.OBJECT_FILTER, attributes, 1);

            if (searchResult.Any())
                return true;

            Log.ErrorFormat("NovellLdapHelper->CheckGroupDn(groupDn: {0}): Wrong Group DN parameter", groupDn);
            return false;
        }

        public override List<LdapObject> GetUsers(string filter = null, int limit = -1)
        {
            var list = new List<LdapObject>();

            try
            {
                if (!string.IsNullOrEmpty(Settings.UserFilter) && !Settings.UserFilter.StartsWith("(") &&
                    !Settings.UserFilter.EndsWith(")"))
                {
                    Settings.UserFilter = string.Format("({0})", Settings.UserFilter);
                }

                if (!string.IsNullOrEmpty(filter) && !filter.StartsWith("(") &&
                    !filter.EndsWith(")"))
                {
                    filter = string.Format("({0})", Settings.UserFilter);
                }

                var searchfilter = string.IsNullOrEmpty(filter)
                    ? Settings.UserFilter
                    : string.Format("(&{0}{1})", Settings.UserFilter, filter);

                list = LDAPSearcher.Search(Settings.UserDN, NovellLdapSearcher.LdapScope.Sub, searchfilter, limit: limit);

                return list;
            }
            catch (Exception e)
            {
                Log.ErrorFormat("NovellLdapHelper->GetUsers(filter: '{0}' limit: {1}) failed. Error: {2}",
                    filter, limit, e);
            }

            return list;
        }

        public override LdapObject GetUserBySid(string sid)
        {
            try
            {
                var ldapUniqueIdAttribute = ConfigurationManager.AppSettings["ldap.unique.id"];

                Criteria criteria;

                if (ldapUniqueIdAttribute == null)
                {
                    criteria = Criteria.Any(
                        Expression.Equal(LdapConstants.RfcLDAPAttributes.ENTRY_UUID, sid),
                        Expression.Equal(LdapConstants.RfcLDAPAttributes.NS_UNIQUE_ID, sid),
                        Expression.Equal(LdapConstants.RfcLDAPAttributes.GUID, sid),
                        Expression.Equal(LdapConstants.ADSchemaAttributes.OBJECT_SID, sid)
                        );
                }
                else
                {
                    criteria = Criteria.All(Expression.Equal(ldapUniqueIdAttribute, sid));
                }

                var searchfilter = string.Format("(&{0}{1})", Settings.UserFilter, criteria);

                var list = LDAPSearcher.Search(Settings.UserDN, NovellLdapSearcher.LdapScope.Sub, searchfilter, limit: 1);

                return list.FirstOrDefault();
            }
            catch (Exception e)
            {
                Log.ErrorFormat("NovellLdapHelper->GetUserBySid(sid: '{0}') failed. Error: {1}", sid, e);
            }

            return null;
        }

        public override List<LdapObject> GetGroups(Criteria criteria = null)
        {
            var list = new List<LdapObject>();

            try
            {
                if (!string.IsNullOrEmpty(Settings.GroupFilter) && !Settings.GroupFilter.StartsWith("(") &&
                    !Settings.GroupFilter.EndsWith(")"))
                {
                    Settings.GroupFilter = string.Format("({0})", Settings.GroupFilter);
                }

                var searchfilter = criteria == null
                    ? Settings.GroupFilter
                    : string.Format("(&{0}{1})", Settings.GroupFilter, criteria);


                list = LDAPSearcher.Search(Settings.GroupDN, NovellLdapSearcher.LdapScope.Sub, searchfilter);
            }
            catch (Exception e)
            {
                Log.ErrorFormat("NovellLdapHelper->GetGroups(criteria: '{0}') failed. Error: {1}", criteria, e);
            }

            return list;
        }

        public override void Dispose()
        {
            LDAPSearcher.Dispose();
        }
    }
}
