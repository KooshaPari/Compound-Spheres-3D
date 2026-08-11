# Compound Spheres!

Compound Spheres is a unity tool for rendering 2d grids on 3d objects, it allows you to render these 2d tiles as any mesh, with their own rotation, scale and position. it also has 2 default data buffers, textures and colors, which provide the texture and color of each tile. you can easily add your own custom buffers too! 
Note: this was created for unity 2022.3, other versions might be incompatible

# Configuration
the sphere manager creator requires you to input your own configuration (spheremanagersettings) this class stores delegates that calculate the positions, rotations, scales, colors, textures, etc for each tile, and its YOU who provides the delegates! the class DefaultSettings has some delegates for you to use on the fly.
of course, you also need to input the mesh and material, the material MUST have the compound sphere shader or another shader with similar functionality.


## Creating the sphere manager

here is an example of how to create a sphere manager

    using CompoundSpheres;
    using UnityEngine;
    Material CompoundSphereMaterial = null;
    int Cols = 64;
    int Rows = 64;
    
    SphereManagerSettings settings = new SphereManagerSettings(
        DefaultSettings.CylindricalInitiation,
        DefaultSettings.CartesianToCylindrical,
        DefaultSettings.CylindricalRotation,
        DefaultSettings.DefaultScale,
        DefaultSettings.DefaultColor,
        DefaultSettings.DefaultTextureIndex,
        DefaultSettings.DefaultMode,
        new Texture2D[]
        {
            DefaultSettings.DefaultTexture
        },
        DefaultSettings.DefaultFormat,
        DefaultSettings.DefaultMesh,
        CompoundSphereMaterial,
        DefaultSettings.DefaultRange,
        new List<IBufferData>() { new CustomBufferData<Vector3>("AddedColors", 12, SphereTileAddedColor) }
    );
    
    SphereManager Manager = SphereManager.Creator.CreateSphereManager(Rows, Cols, settings, "My New Sphere Manager");
    
    //finish
    Manager.Destroy();
the CompoundSphereMaterial must be provided by you, this material must have the compound sphere Shader or another shader with same functionality. you may find this material in the [default assets folder](https://github.com/MelvinShwuaner/Compound-Spheres/tree/main/Default%20Assets)
the compound sphere mesh is a box, with no face under it to squeeze a little more fps
## Drawing your tiles
for more performance, compound spheres renders all of the tiles in groups, called rows. the rows are on the X axis,  compound spheres has a setting called camera range, which is the range of tiles around the camera, for example if the camera's X position is 10 and the camera range is -10, 10
rows from 0 to 20 will be drawn. don't worry if it "overflows" as it will be clamped. for example if the camera range is -30, 30 and the X axis's length is 64, then it will be from 44 -> through zero -> 40, so 60 rows are drawn.
the range function that calculates this is provided in the settings, since its a function, you can change the range very easily!

    static void CameraRange(SphereManager manager, out int Min, out int Max)
    {
        Min = -(manager.Rows / 4); Max = manager.Rows / 4;
    }
    
    int CameraX = 2;
    Manager.DrawTiles(CameraX);
this is because, not all rows are visible, for example if you are rendering it on a cylinder only half can be visible to the camera on a time.
or you could just call DrawAllTiles...
## Adding custom buffers
if you have a custom shader that also lets you make the tiles glow, and you want to store the amount of glow each tile gives off, this is how!

    float GetGlow(SphereTile Tile){.......}
    SphereManagerSettings settings = new SphereManagerSettings(
        DefaultSettings.CylindricalInitiation,
        DefaultSettings.CartesianToCylindrical,
        DefaultSettings.CylindricalRotation,
        DefaultSettings.DefaultScale,
        DefaultSettings.DefaultColor,
        DefaultSettings.DefaultTextureIndex,
        DefaultSettings.DefaultMode,
        new Texture2D[]
        {
            DefaultSettings.DefaultTexture
        },
        DefaultSettings.DefaultFormat,
        DefaultSettings.DefaultMesh,
        CompoundSphereMaterial,
        DefaultSettings.DefaultRange,
        new List<IBufferData>() { new CustomBufferData<float>("TileGlows", GetGlow) }
    );
normally you have to manually release buffer memory, but here you dont! the manager will automatically release it once you destroy the manager.
the last paramater is the function that returns the buffers type for any spheretile. use manager.updatecustom and manager.refreshcustom to update the custom buffer

## Sphere tiles
sphere tiles are readonly, you cannot change their position and rotation once created, but their scales, texture and color are provided by a function, so every time they are updated the function is called. this because if you move it / rotate it a gap can form in the sphere!

    foreach(SphereRow row in Manager)
    {
        foreach(SphereTile tile in row)
        {
            Debug.Log(tile.Position);
        }
    }
