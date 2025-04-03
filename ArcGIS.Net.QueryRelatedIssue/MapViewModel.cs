using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;

namespace ArcGIS.Net.QueryRelatedIssue;

/// <summary>
/// Provides map data to an application
/// </summary>
public partial class MapViewModel : ObservableObject
{
    public MapViewModel()
    {
        _map = new Map(SpatialReferences.WebMercator)
        {
            InitialViewpoint = new Viewpoint(
                new Envelope(-180, -85, 180, 85, SpatialReferences.Wgs84)
            ),
            Basemap = new Basemap(BasemapStyle.ArcGISStreets)
        };

        _ = Initialize();
    }

    private string Light1GlobalId = "{F9158A1D-A1BF-4110-B964-6B25ABC0E143}";

    [RelayCommand]
    private async Task OnReloadRelatedFeatures()
    {
        await LoadRelatedFeatures();
    }

    public async Task LoadRelatedFeatures()
    {
        // relevant tables: Lys and OverettlinjeLys
        // - "Lys" means light in English)
        // - I do not know what does "Overett" mean in English so no translations are used here. In reality it is a nautical navigational function where 2 lights placed at distance and at different altitudes over sea level create a fictional line that can be used for navigation
        // There are 2 relationships from the OverettlinjeLys table to the Lys table
        // named OverettlinjeLys (field FKNavinst1 = Lys.GlobalId)
        //   and OverettlinjeLys2 (field FKNavinst2 = Lys.GlobalId)
        // The relationships are shown in the ArcGIS Pro screenshot in the 'data' folder, "OverettLinjeLys-relationships.png"

        // My problem is that while ArcGIS Pro is showing 2 relationships, each showing its own feature,
        // see "OverettLinjeLys-feature-attriburtes-with-related-features.png" in the 'data' folder,
        // calling QueryRelatedFeaturesAsync on the Overettlinje feature returns 2 results, each containing THE SAME Lys feature
        await GetOverettAndQueryRelatedLights();

        // The above problem is then likely causing that querying Overettlinje related to the Lys that is not in the results above returns 0 features
        await GetLightAndQueryRelatedOverett();
    }

    private async Task GetOverettAndQueryRelatedLights()
    {
        // first get the overett feature
        var overettTable = gdb.GeodatabaseFeatureTables.First(x =>
            x.TableName == "OverettlinjeLys"
        );
        var overettQuery = new QueryParameters()
        {
            WhereClause = $"FKNavinst1 = '{Light1GlobalId}'"
        };

        var overettQueryResults = await overettTable.QueryFeaturesAsync(overettQuery);
        var overettFeature = overettQueryResults.First() as ArcGISFeature;

        // FKNavinst1 and FKNavinst2 clearly contain different values
        Debug.WriteLine("Overett FKNavInst1: " + overettFeature.Attributes["FKNavInst1"]);
        Debug.WriteLine("Overett FKNavInst2: " + overettFeature.Attributes["FKNavInst2"]);

        // now query for related features
        var relatedResults = await overettTable.QueryRelatedFeaturesAsync(overettFeature);

        // Here we are getting THE SAME FEATURE in each of the 2 relationships
        // Console output:
        /*
            Relationship: Lys, key field: FKNavInst1, related table Lys, related feature count: 1, globalid: 946aaa97-de68-48d2-b259-5e955f1693d9
            Relationship: Lys2, key field: FKNavInst2, related table Lys, related feature count: 1, globalid: 946aaa97-de68-48d2-b259-5e955f1693d9 -- see same as above ?!
         */

        foreach (var relatedResult in relatedResults.Where(x => x.RelatedTable.TableName == "Lys"))
        {
            var globalId = relatedResult.FirstOrDefault()?.Attributes["globalid"];

            Debug.WriteLine(
                "Relationship: "
                    + relatedResult.RelationshipInfo.Name
                    + ", key field: "
                    + relatedResult.RelationshipInfo.KeyField
                    + ", related table "
                    + relatedResult.RelatedTable.TableName
                    + ", related feature count: "
                    + relatedResult.Count()
                    + ", globalid: "
                    + globalId
            );
        }
    }

    private async Task GetLightAndQueryRelatedOverett()
    {
        var lightsTable = gdb.GeodatabaseFeatureTables.First(x => x.TableName == "Lys");
        var lightQuery = new QueryParameters() { WhereClause = $"globalid = '{Light1GlobalId}'" };
        var lightQueryResults = await lightsTable.QueryFeaturesAsync(lightQuery);

        var lightFeature = lightQueryResults.First() as ArcGISFeature;
        var relatedResults = await lightsTable.QueryRelatedFeaturesAsync(lightFeature);

        Debug.WriteLine("Related tabled names:");
        foreach (var r in relatedResults)
        {
            Debug.WriteLine(" " + r.RelatedTable.TableName);
        }

        foreach (
            var relatedResult in relatedResults.Where(x =>
                x.RelatedTable.TableName == "OverettlinjeLys"
            )
        )
        {
            Debug.WriteLine(
                "Relationship: "
                    + relatedResult.RelationshipInfo.Name
                    + ", related table "
                    + relatedResult.RelatedTable.TableName
                    + ", related feature count: "
                    + relatedResult.Count()
            );
        }
        // the result is 0 features in each relationship

        // But I know there is in fact a feature in the 1st relationship
        var overettTable = gdb.GeodatabaseFeatureTables.First(x =>
            x.TableName == "OverettlinjeLys"
        );
        var overettQuery = new QueryParameters()
        {
            WhereClause = $"FKNavinst1 = '{Light1GlobalId}'"
        };
        var overettQueryResults = await overettTable.QueryFeaturesAsync(overettQuery);
        var manualRelatedFeatures = overettQueryResults.ToList(); // 1 feature

        Debug.WriteLine("Manually retrieved related features: " + manualRelatedFeatures.Count);
    }

    private Geodatabase gdb;

    private async Task Initialize()
    {
        gdb = await GetGeodatabase();
        await LoadAllTablesToMap(gdb);
        await LoadRelatedFeatures();
    }

    private async Task LoadAllTablesToMap(Geodatabase gdb)
    {
        foreach (var table in gdb.GeodatabaseFeatureTables)
        {
            await table.LoadAsync();
            var layer = new FeatureLayer(table);
            await layer.LoadAsync();
            Map.OperationalLayers.Add(layer);
        }
    }

    private async Task<Geodatabase> GetGeodatabase()
    {
        var executingPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        var gdbPath = Path.Combine(
            executingPath.Replace(
                "ArcGIS.Net.QueryRelatedIssue\\bin\\Debug\\net8.0-windows10.0.19041.0",
                ""
            ),
            "data",
            "gdb.geodatabase"
        );
        return await Geodatabase.OpenAsync(gdbPath);
    }

    [ObservableProperty]
    private Map _map;
}