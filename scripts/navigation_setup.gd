extends NavigationRegion3D
## Runtime-bake navmesh from Geometry static colliders only (no GPU mesh readback).

signal navigation_ready

@export var geometry_root_path: NodePath = NodePath("../Geometry")
@export var agent_radius: float = 0.5
@export var agent_height: float = 1.5
@export var cell_size: float = 0.25
@export var cell_height: float = 0.25

var nav_bake_done: bool = false
var polygon_count: int = 0


func _ready() -> void:
	add_to_group("nav_setup")
	call_deferred("_bake")


func _bake() -> void:
	var geom_root: Node = get_node_or_null(geometry_root_path)
	if geom_root == null:
		push_warning("NavigationSetup: geometry root missing: %s" % geometry_root_path)
		nav_bake_done = true
		navigation_ready.emit()
		return

	var nav_mesh := NavigationMesh.new()
	nav_mesh.agent_radius = agent_radius
	nav_mesh.agent_height = agent_height
	nav_mesh.agent_max_climb = 0.25
	nav_mesh.agent_max_slope = 45.0
	nav_mesh.cell_size = cell_size
	nav_mesh.cell_height = cell_height
	# Colliders only — avoids runtime GPU mesh stalls / blank-frame freezes.
	nav_mesh.geometry_parsed_geometry_type = NavigationMesh.PARSED_GEOMETRY_STATIC_COLLIDERS
	nav_mesh.geometry_collision_mask = 1
	nav_mesh.geometry_source_geometry_mode = NavigationMesh.SOURCE_GEOMETRY_ROOT_NODE_CHILDREN

	var source := NavigationMeshSourceGeometryData3D.new()
	NavigationServer3D.parse_source_geometry_data(nav_mesh, source, geom_root)
	NavigationServer3D.bake_from_source_geometry_data(nav_mesh, source)
	navigation_mesh = nav_mesh
	polygon_count = nav_mesh.get_polygon_count()
	if polygon_count <= 0:
		push_warning("NavigationSetup: bake produced 0 polygons — agents will use direct move fallback.")

	await get_tree().physics_frame
	await get_tree().physics_frame
	nav_bake_done = true
	navigation_ready.emit()
