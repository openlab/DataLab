using System;
using System.Web.Routing;
using System.Web;
using System.Net;
using System.Xml.Linq;
using System.Xml;
using System.Linq;

namespace Ogdi.DataServices 
{
	public class MetaDataHttpHandler : TableStorageHttpHandlerBase, IHttpHandler
	{
		public string OgdiAlias { get; set; }        

		private readonly string START_DATASERVICES_TEMPLATE =
@"<?xml version='1.0' encoding='utf-8' standalone='yes'?>
<edmx:Edmx Version='1.0' xmlns:edmx='http://schemas.microsoft.com/ado/2007/06/edmx'>
  <edmx:DataServices>
	<Schema Namespace='" + AppSettings.RootServiceNamespace + @".{0}' xmlns:d='http://schemas.microsoft.com/ado/2007/08/dataservices' xmlns:m='http://schemas.microsoft.com/ado/2007/08/dataservices/metadata' xmlns='http://schemas.microsoft.com/ado/2006/04/edm'>
	  <EntityContainer Name='{0}DataService' m:IsDefaultEntityContainer='true'>
";

		private readonly string ENTITYSET_TEMPLATE =
@"        <EntitySet Name='{0}' EntityType='" + AppSettings.RootServiceNamespace + @".{1}.{2}' />
";

//        private const string END_ENTITYCONTAINER_TEMPLATE =
//@"      </EntityContainer>
//    </Schema>
//";
		private const string END_ENTITYCONTAINER_TEMPLATE =
@"      </EntityContainer>
";

//        private readonly string START_ENTITYTYPESCHEMA_TEMPLATE =
//@"    <Schema Namespace='" + AppSettings.RootServiceNamespace + @".{0}' xmlns:d='http://schemas.microsoft.com/ado/2007/08/dataservices' xmlns:m='http://schemas.microsoft.com/ado/2007/08/dataservices/metadata' xmlns='http://schemas.microsoft.com/ado/2006/04/edm'>
//";

		private const string START_ENTITYTYPE_TEMPLATE =
@"      <EntityType Name='{0}'>
		<Key>
		  <PropertyRef Name='PartitionKey' />
		  <PropertyRef Name='RowKey' />
		</Key>
		<Property Name='PartitionKey' Type='Edm.String' Nullable='false' />
		<Property Name='RowKey' Type='Edm.String' Nullable='false' />
		<Property Name='Timestamp' Type='Edm.DateTime' Nullable='false' />
		<Property Name='entityid' Type='Edm.Guid' Nullable='false' />
";

		private const string START_PROPERTY_TEMPLATE =
@"        <Property Name='{0}' Type='{1}' Nullable='true' />
";

		private const string END_ENTITYTYPE_TEMPLATE =
@"      </EntityType>
";

		private const string END_ENTITYTYPESCHEMA_TEMPLATE =
@"    </Schema>
";

		private const string END_DATASERVICES_TEMPLATE =
@"  </edmx:DataServices>
</edmx:Edmx>";

		#region IHttpHandler Members

		public bool IsReusable
		{
			get { return true; }
		}

		public void ProcessRequest(HttpContext context)
		{
			if (!this.IsHttpGet(context))
			{
				this.RespondForbidden(context);
			}
			else
			{
				context.Response.Headers["DataServiceVersion"] = "1.0;";
				context.Response.CacheControl = "no-cache";
				context.Response.ContentType = "application/xml;charset=utf-8";

				var requestUrl = AppSettings.TableStorageBaseUrl + "EntityMetadata";
				WebRequest request = this.CreateTableStorageSignedRequest(context,
                                                                          AppSettings.ParseStorageAccount(
                                                                            AppSettings.EnabledStorageAccounts[OgdiAlias].storageaccountname,
                                                                            AppSettings.EnabledStorageAccounts[OgdiAlias].storageaccountkey),
																		  requestUrl, false);

				try
				{
					var response = request.GetResponse();
					var responseStream = response.GetResponseStream();

					var feed = XElement.Load(XmlReader.Create(responseStream));

					context.Response.Write(string.Format(START_DATASERVICES_TEMPLATE, OgdiAlias));

					var propertiesElements =  feed.Elements(_nsAtom + "entry").Elements(_nsAtom + "content").Elements(_nsm + "properties");
					
					foreach (var e in propertiesElements)
                    {
                        if (e == null) continue;

						// Changed to use the simple approach of representing "entitykind" as
						// "entityset" value plus the text "Item."  A decision was made to do
						// this at the service level for now so that we wouldn't have to deal 
						// with changing the data import code and the existing values in the 
						// EntityMetadata table.
						// New notice: Import code was changed, so entityKind = entitySet + "Item" in storage for all new data
						// So return the code back:

						var entitySet = e.Element(_nsd + "entityset");
						//var entityKind = entitySet + "Item";
						var entityKind = e.Element(_nsd + "entitykind");

                        context.Response.Write(string.Format(ENTITYSET_TEMPLATE,
                                                             entitySet != null ? entitySet.Value : string.Empty,
                                                             OgdiAlias,
                                                             entityKind != null ? entityKind.Value : string.Empty));
					}

					context.Response.Write(END_ENTITYCONTAINER_TEMPLATE);
					//context.Response.Write(string.Format(START_ENTITYTYPESCHEMA_TEMPLATE, this.OgdiAlias));

					foreach (var e in propertiesElements)
                    {
                        if (e == null) continue;
						//var entityKind = e.Element(_nsd + "entityset").Value + "Item";
						var entityKind = e.Element(_nsd + "entitykind").Value;

						context.Response.Write(string.Format(START_ENTITYTYPE_TEMPLATE, entityKind));

						e.Elements(_nsd + "PartitionKey").Remove();
						e.Elements(_nsd + "RowKey").Remove();
						e.Elements(_nsd + "Timestamp").Remove();
						e.Elements(_nsd + "entityset").Remove();
						e.Elements(_nsd + "entitykind").Remove();
						e.Elements(_nsd + "entityid").Remove();

						foreach (XElement prop in e.Elements())
						{	
							context.Response.Write(string.Format(START_PROPERTY_TEMPLATE, 
												   prop.Name.LocalName, 
												   prop.Value.Replace("System","Edm")));
						}

						context.Response.Write(END_ENTITYTYPE_TEMPLATE);
					}

					context.Response.Write(END_ENTITYTYPESCHEMA_TEMPLATE);
					context.Response.Write(END_DATASERVICES_TEMPLATE);	
				}
				catch (WebException ex)
				{
					var response = ex.Response as HttpWebResponse;
					context.Response.StatusCode = (int)response.StatusCode;
					context.Response.End();
				}
			}
		}

		#endregion
	}
}
