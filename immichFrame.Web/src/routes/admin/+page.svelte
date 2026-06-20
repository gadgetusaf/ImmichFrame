<script lang="ts">
	import { onMount } from 'svelte';
	import {
		getSetupRequired,
		getMe,
		login,
		setup,
		logout,
		getGeneralSettings,
		saveGeneralSettings,
		type GeneralSettings
	} from '$lib/adminApi';
	import AccountsPanel from '$lib/components/admin/AccountsPanel.svelte';
	import LinksPanel from '$lib/components/admin/LinksPanel.svelte';

	type Field = {
		key: string;
		label: string;
		type: 'bool' | 'number' | 'text' | 'password' | 'select' | 'color';
		step?: string;
		options?: string[];
		help?: string;
	};
	type Group = { title: string; fields: Field[] };

	const GROUPS: Group[] = [
		{
			title: 'Slideshow',
			fields: [
				{ key: 'interval', label: 'Interval (seconds)', type: 'number', step: '1' },
				{ key: 'transitionDuration', label: 'Transition duration (seconds)', type: 'number', step: '0.1' },
				{ key: 'layout', label: 'Layout', type: 'select', options: ['splitview', 'single'] },
				{ key: 'style', label: 'Background style', type: 'select', options: ['none', 'solid', 'transition', 'blur'] },
				{ key: 'imageZoom', label: 'Image zoom (Ken Burns)', type: 'bool' },
				{ key: 'imagePan', label: 'Image pan', type: 'bool' },
				{ key: 'imageFill', label: 'Fill screen (may crop)', type: 'bool' },
				{ key: 'playAudio', label: 'Play video audio', type: 'bool' },
				{ key: 'downloadImages', label: 'Allow image download', type: 'bool' },
				{ key: 'renewImagesDuration', label: 'Renew images after (days)', type: 'number', step: '1' }
			]
		},
		{
			title: 'Clock & progress',
			fields: [
				{ key: 'showClock', label: 'Show clock', type: 'bool' },
				{ key: 'clockFormat', label: 'Clock format', type: 'text' },
				{ key: 'clockDateFormat', label: 'Clock date format', type: 'text' },
				{ key: 'showProgressBar', label: 'Show progress bar', type: 'bool' }
			]
		},
		{
			title: 'Photo info',
			fields: [
				{ key: 'showPhotoDate', label: 'Show photo date', type: 'bool' },
				{ key: 'photoDateFormat', label: 'Photo date format', type: 'text' },
				{ key: 'showImageDesc', label: 'Show description', type: 'bool' },
				{ key: 'showPeopleDesc', label: 'Show people', type: 'bool' },
				{ key: 'showTagsDesc', label: 'Show tags', type: 'bool' },
				{ key: 'showAlbumName', label: 'Show album name', type: 'bool' },
				{ key: 'showImageLocation', label: 'Show location', type: 'bool' },
				{ key: 'imageLocationFormat', label: 'Location format', type: 'text' }
			]
		},
		{
			title: 'Appearance',
			fields: [
				{ key: 'primaryColor', label: 'Primary color', type: 'color' },
				{ key: 'secondaryColor', label: 'Secondary color', type: 'color' },
				{ key: 'baseFontSize', label: 'Base font size', type: 'text', help: 'e.g. 17px' },
				{ key: 'language', label: 'Language', type: 'text', help: 'e.g. en' }
			]
		},
		{
			title: 'Weather',
			fields: [
				{ key: 'weatherApiKey', label: 'OpenWeatherMap API key', type: 'text' },
				{ key: 'weatherLatLong', label: 'Weather location (lat,long)', type: 'text' },
				{ key: 'unitSystem', label: 'Unit system', type: 'select', options: ['imperial', 'metric'] },
				{ key: 'showWeatherDescription', label: 'Show weather description', type: 'bool' },
				{ key: 'weatherIconUrl', label: 'Weather icon URL', type: 'text' }
			]
		},
		{
			title: 'Integration',
			fields: [
				{ key: 'authenticationSecret', label: 'Client auth secret', type: 'password', help: 'Required to view the slideshow when set' },
				{ key: 'webhook', label: 'Webhook URL', type: 'text' },
				{ key: 'refreshAlbumPeopleInterval', label: 'Refresh albums/people (hours)', type: 'number', step: '1' }
			]
		}
	];

	let phase = $state<'loading' | 'setup' | 'login' | 'authed'>('loading');
	let tab = $state<'settings' | 'accounts' | 'links'>('settings');
	let username = $state('');
	let password = $state('');
	let confirmPassword = $state('');
	let authError = $state('');
	let authBusy = $state(false);

	let currentUser = $state('');
	let settings = $state<GeneralSettings | null>(null);
	let webcalendarsText = $state('');
	let saveState = $state<'idle' | 'saving' | 'saved' | 'error'>('idle');
	let saveError = $state('');

	onMount(async () => {
		try {
			if (await getSetupRequired()) {
				phase = 'setup';
				return;
			}
			const user = await getMe();
			if (user) {
				currentUser = user.username;
				await loadSettings();
				phase = 'authed';
			} else {
				phase = 'login';
			}
		} catch {
			phase = 'login';
		}
	});

	async function loadSettings() {
		const loaded = await getGeneralSettings();
		settings = loaded;
		webcalendarsText = ((loaded.webcalendars as string[]) ?? []).join('\n');
	}

	async function handleAuth(event: SubmitEvent) {
		event.preventDefault();
		authError = '';
		if (phase === 'setup' && password !== confirmPassword) {
			authError = 'Passwords do not match.';
			return;
		}
		authBusy = true;
		try {
			const result = phase === 'setup' ? await setup(username, password) : await login(username, password);
			if (result.ok) {
				currentUser = result.username ?? username;
				password = '';
				confirmPassword = '';
				await loadSettings();
				phase = 'authed';
			} else {
				authError = result.message ?? 'Authentication failed.';
			}
		} catch {
			authError = 'Could not reach the server.';
		} finally {
			authBusy = false;
		}
	}

	async function handleSave() {
		if (!settings) return;
		saveState = 'saving';
		saveError = '';
		try {
			settings.webcalendars = webcalendarsText
				.split('\n')
				.map((s) => s.trim())
				.filter((s) => s.length > 0);
			const updated = await saveGeneralSettings(settings);
			settings = updated;
			webcalendarsText = ((updated.webcalendars as string[]) ?? []).join('\n');
			saveState = 'saved';
			setTimeout(() => {
				if (saveState === 'saved') saveState = 'idle';
			}, 2500);
		} catch (e) {
			saveState = 'error';
			saveError = e instanceof Error ? e.message : 'Failed to save settings.';
		}
	}

	async function handleLogout() {
		await logout();
		settings = null;
		username = '';
		password = '';
		phase = 'login';
	}
</script>

<svelte:head><title>ImmichFrame · Admin</title></svelte:head>

<div class="min-h-screen w-full bg-slate-900 text-slate-100">
	{#if phase === 'loading'}
		<div class="flex h-screen items-center justify-center text-slate-400">Loading…</div>
	{:else if phase === 'setup' || phase === 'login'}
		<div class="flex min-h-screen items-center justify-center p-4">
			<form
				onsubmit={handleAuth}
				class="w-full max-w-sm space-y-4 rounded-xl border border-slate-700 bg-slate-800 p-6 shadow-xl"
			>
				<h1 class="text-xl font-semibold">
					{phase === 'setup' ? 'Create admin account' : 'Admin sign in'}
				</h1>
				{#if phase === 'setup'}
					<p class="text-sm text-slate-400">No administrator exists yet. Create the first account.</p>
				{/if}
				<label class="block text-sm">
					<span class="mb-1 block text-slate-300">Username</span>
					<input
						bind:value={username}
						autocomplete="username"
						required
						class="w-full rounded-md border border-slate-600 bg-slate-700 px-3 py-2 outline-none focus:border-indigo-400"
					/>
				</label>
				<label class="block text-sm">
					<span class="mb-1 block text-slate-300">Password</span>
					<input
						type="password"
						bind:value={password}
						autocomplete={phase === 'setup' ? 'new-password' : 'current-password'}
						required
						class="w-full rounded-md border border-slate-600 bg-slate-700 px-3 py-2 outline-none focus:border-indigo-400"
					/>
				</label>
				{#if phase === 'setup'}
					<label class="block text-sm">
						<span class="mb-1 block text-slate-300">Confirm password</span>
						<input
							type="password"
							bind:value={confirmPassword}
							autocomplete="new-password"
							required
							class="w-full rounded-md border border-slate-600 bg-slate-700 px-3 py-2 outline-none focus:border-indigo-400"
						/>
					</label>
				{/if}
				{#if authError}
					<p class="text-sm text-red-400">{authError}</p>
				{/if}
				<button
					type="submit"
					disabled={authBusy}
					class="w-full rounded-md bg-indigo-600 px-3 py-2 font-medium hover:bg-indigo-500 disabled:opacity-50"
				>
					{authBusy ? 'Please wait…' : phase === 'setup' ? 'Create account' : 'Sign in'}
				</button>
			</form>
		</div>
	{:else if phase === 'authed' && settings}
		<div class="mx-auto max-w-4xl p-4 pb-24">
			<header class="sticky top-0 z-10 -mx-4 mb-6 flex items-center justify-between gap-4 border-b border-slate-700 bg-slate-900/95 px-4 py-3 backdrop-blur">
				<nav class="flex gap-1">
					<button onclick={() => (tab = 'settings')} class="rounded-md px-3 py-1.5 text-sm {tab === 'settings' ? 'bg-slate-700 font-medium' : 'text-slate-400 hover:bg-slate-800'}">Display settings</button>
					<button onclick={() => (tab = 'accounts')} class="rounded-md px-3 py-1.5 text-sm {tab === 'accounts' ? 'bg-slate-700 font-medium' : 'text-slate-400 hover:bg-slate-800'}">Accounts</button>
					<button onclick={() => (tab = 'links')} class="rounded-md px-3 py-1.5 text-sm {tab === 'links' ? 'bg-slate-700 font-medium' : 'text-slate-400 hover:bg-slate-800'}">Links</button>
				</nav>
				<div class="flex items-center gap-3">
					<span class="hidden text-sm text-slate-400 sm:inline">{currentUser}</span>
					<button onclick={handleLogout} class="rounded-md border border-slate-600 px-3 py-1.5 text-sm hover:bg-slate-800">
						Sign out
					</button>
					{#if tab === 'settings'}
						<button
							onclick={handleSave}
							disabled={saveState === 'saving'}
							class="rounded-md bg-indigo-600 px-4 py-1.5 text-sm font-medium hover:bg-indigo-500 disabled:opacity-50"
						>
							{saveState === 'saving' ? 'Saving…' : saveState === 'saved' ? 'Saved ✓' : 'Save'}
						</button>
					{/if}
				</div>
			</header>

			{#if tab === 'settings'}
			{#if saveState === 'error'}
				<p class="mb-4 rounded-md border border-red-500/40 bg-red-500/10 px-3 py-2 text-sm text-red-300">{saveError}</p>
			{/if}

			<div class="space-y-6">
				{#each GROUPS as group}
					<section class="rounded-xl border border-slate-700 bg-slate-800/60 p-5">
						<h2 class="mb-4 text-sm font-semibold uppercase tracking-wide text-slate-400">{group.title}</h2>
						<div class="grid grid-cols-1 gap-x-6 gap-y-4 sm:grid-cols-2">
							{#each group.fields as field}
								{#if field.type === 'bool'}
									<label class="flex items-center gap-3 text-sm sm:col-span-1">
										<input type="checkbox" bind:checked={settings[field.key] as boolean} class="h-4 w-4 rounded border-slate-600 bg-slate-700" />
										<span>{field.label}</span>
									</label>
								{:else}
									<label class="block text-sm">
										<span class="mb-1 block text-slate-300">{field.label}</span>
										{#if field.type === 'select'}
											<select bind:value={settings[field.key]} class="w-full rounded-md border border-slate-600 bg-slate-700 px-3 py-2 outline-none focus:border-indigo-400">
												{#each field.options ?? [] as option}
													<option value={option}>{option}</option>
												{/each}
											</select>
										{:else if field.type === 'number'}
											<input type="number" step={field.step ?? '1'} bind:value={settings[field.key]} class="w-full rounded-md border border-slate-600 bg-slate-700 px-3 py-2 outline-none focus:border-indigo-400" />
										{:else if field.type === 'color'}
											<div class="flex items-center gap-2">
												<span class="h-8 w-8 shrink-0 rounded border border-slate-600" style="background-color: {(settings[field.key] as string) || 'transparent'}"></span>
												<input type="text" placeholder="#rrggbb" bind:value={settings[field.key]} class="w-full rounded-md border border-slate-600 bg-slate-700 px-3 py-2 outline-none focus:border-indigo-400" />
											</div>
										{:else}
											<input type={field.type === 'password' ? 'password' : 'text'} bind:value={settings[field.key]} class="w-full rounded-md border border-slate-600 bg-slate-700 px-3 py-2 outline-none focus:border-indigo-400" />
										{/if}
										{#if field.help}<span class="mt-1 block text-xs text-slate-500">{field.help}</span>{/if}
									</label>
								{/if}
							{/each}
						</div>
					</section>
				{/each}

				<section class="rounded-xl border border-slate-700 bg-slate-800/60 p-5">
					<h2 class="mb-4 text-sm font-semibold uppercase tracking-wide text-slate-400">Web calendars</h2>
					<label class="block text-sm">
						<span class="mb-1 block text-slate-300">Calendar URLs (one per line)</span>
						<textarea bind:value={webcalendarsText} rows="3" class="w-full rounded-md border border-slate-600 bg-slate-700 px-3 py-2 font-mono text-xs outline-none focus:border-indigo-400"></textarea>
					</label>
				</section>
			</div>
			{:else if tab === 'accounts'}
				<AccountsPanel />
			{:else}
				<LinksPanel />
			{/if}
		</div>
	{/if}
</div>
