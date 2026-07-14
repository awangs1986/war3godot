extends SceneTree
func _initialize():
	call_deferred("r")
func r():
	var W3E = load("res://addons/w3kit/w3e_map.gd")
	var path = ProjectSettings.globalize_path("res://war3/maps/DotA_v6.80c/war3map.w3e")
	var map = W3E.load_file(path)
	var n=0; var samples=[]
	var omin=INF; var omax=-INF
	for y in map.tile_count_y():
		for x in map.tile_count_x():
			var c00=map.get_corner(x,y)
			if not c00.water: continue
			n+=1
			var wy=(c00.water_height-0.7)*128.0
			var gy=c00.world_y()
			var d=wy-gy
			omin=minf(omin,d); omax=maxf(omax,d)
			if samples.size()<8:
				samples.append("x%d y%d wh=%.3f wy=%.1f gy=%.1f d=%.1f L=%d"%[x,y,c00.water_height,wy,gy,d,c00.layer_height])
	print("water_corners~tiles",n," wy-gy range",omin,"..",omax)
	for s in samples: print(" ",s)
	print("map size", map.tile_count_x(),"x",map.tile_count_y()," center", map.center_offset)
	quit(0)
