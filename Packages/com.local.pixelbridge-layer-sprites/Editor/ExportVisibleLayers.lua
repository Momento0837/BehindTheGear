-- This batch document is disposable. Visibility changes are never saved to the source.
local sprite = assert(app.sprite, "Open an Aseprite document first.")
local output = assert(app.params.output)
local layers = {}
local function hex(text)
  return (text:gsub(".", function(c) return string.format("%02x", string.byte(c)) end))
end

local function collect(children, parentKey, parentName)
  local occurrences = {}
  for _, layer in ipairs(children) do
    local name = layer.name
    -- Count hidden siblings too, so toggling an eye never changes another layer's key.
    occurrences[name] = (occurrences[name] or 0) + 1
    local ordinal = occurrences[name]
    if layer.isReference then
      layer.isVisible = false
    elseif layer.isVisible then
      local key = parentKey .. "/" .. hex(name) .. "." .. ordinal
      local label = name ~= "" and name or "(unnamed)"
      if ordinal > 1 then label = label .. " (" .. ordinal .. ")" end
      if parentName ~= "" then label = parentName .. " / " .. label end
      if layer.isGroup then
        collect(layer.layers, key, label)
      else
        layers[#layers + 1] = { layer=layer, key=key, name=label }
        layer.isVisible = false
      end
    end
  end
end

collect(sprite.layers, "", "")
local rows = {}
for _, entry in ipairs(layers) do
  if entry.layer:cel(1) then
    entry.layer.isVisible = true
    local image = Image(sprite.width, sprite.height, ColorMode.RGB)
    image:drawSprite(sprite, 1)
    if not image:isEmpty() then
      local index = #rows + 1
      image:saveAs(app.fs.joinPath(output, index .. ".png"))
      rows[index] = entry.key .. "\t" .. hex(entry.name) .. "\n"
    end
    entry.layer.isVisible = false
  end
end
local manifest = assert(io.open(app.fs.joinPath(output, "layers.txt"), "wb"))
assert(manifest:write(table.concat(rows)))
assert(manifest:close())
