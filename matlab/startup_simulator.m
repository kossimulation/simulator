function startup_simulator()
%STARTUP_SIMULATOR Add project folders to the MATLAB path and load parameters
%   into the base workspace (fw, heli) so Simulink models can reference them.

root = fileparts(mfilename('fullpath'));
addpath(fullfile(root, 'params'), fullfile(root, 'codegen'), fullfile(root, 'models'));

assignin('base', 'fw', fixed_wing_params());
assignin('base', 'heli', helicopter_params());
assignin('base', 'sim_dt', 0.005); % must match Simulation::max_substep (native)

fprintf('Simulator project ready. Parameters: fw, heli, sim_dt.\n');
end
