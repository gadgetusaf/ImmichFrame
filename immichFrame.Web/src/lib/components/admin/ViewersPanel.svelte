<script lang="ts">
	import { onMount } from 'svelte';
	import { listViewers, createViewer, deleteViewer, type Viewer } from '$lib/adminApi';

	let viewers = $state<Viewer[]>([]);
	let loading = $state(true);
	let error = $state('');

	let username = $state('');
	let password = $state('');
	let creating = $state(false);
	let createError = $state('');

	const inputClass =
		'w-full rounded-md border border-slate-600 bg-slate-700 px-3 py-2 outline-none focus:border-indigo-400';

	onMount(load);

	async function load() {
		loading = true;
		error = '';
		try {
			viewers = await listViewers();
		} catch (e) {
			error = e instanceof Error ? e.message : 'Failed to load viewers.';
		} finally {
			loading = false;
		}
	}

	async function add(event: SubmitEvent) {
		event.preventDefault();
		createError = '';
		if (!username.trim() || !password) {
			createError = 'Username and password are required.';
			return;
		}
		creating = true;
		try {
			await createViewer(username.trim(), password);
			username = '';
			password = '';
			await load();
		} catch (e) {
			createError = e instanceof Error ? e.message : 'Failed to add viewer.';
		} finally {
			creating = false;
		}
	}

	async function remove(viewer: Viewer) {
		if (!confirm(`Delete viewer ${viewer.username}?`)) return;
		try {
			await deleteViewer(viewer.id);
			await load();
		} catch (e) {
			error = e instanceof Error ? e.message : 'Failed to delete viewer.';
		}
	}
</script>

<div class="space-y-4">
	<p class="text-sm text-slate-400">Viewer accounts can open links set to “Viewer login”.</p>

	<form onsubmit={add} class="flex flex-wrap items-end gap-3 rounded-xl border border-slate-700 bg-slate-800/60 p-4">
		<label class="min-w-[10rem] flex-1 text-sm">
			<span class="mb-1 block text-slate-300">Username</span>
			<input bind:value={username} autocomplete="off" class={inputClass} />
		</label>
		<label class="min-w-[10rem] flex-1 text-sm">
			<span class="mb-1 block text-slate-300">Password</span>
			<input type="password" bind:value={password} autocomplete="new-password" class={inputClass} />
		</label>
		<button type="submit" disabled={creating} class="rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium hover:bg-indigo-500 disabled:opacity-50">
			{creating ? 'Adding…' : 'Add viewer'}
		</button>
		{#if createError}<span class="w-full text-sm text-red-400">{createError}</span>{/if}
	</form>

	{#if error}<p class="rounded-md border border-red-500/40 bg-red-500/10 px-3 py-2 text-sm text-red-300">{error}</p>{/if}

	{#if loading}
		<p class="text-slate-400">Loading…</p>
	{:else if viewers.length === 0}
		<div class="rounded-xl border border-dashed border-slate-700 p-8 text-center text-slate-400">No viewer accounts yet.</div>
	{:else}
		{#each viewers as viewer (viewer.id)}
			<div class="flex items-center justify-between rounded-xl border border-slate-700 bg-slate-800/60 p-4">
				<span class="font-medium">{viewer.username}</span>
				<button onclick={() => remove(viewer)} class="rounded-md border border-red-500/40 px-3 py-1.5 text-sm text-red-300 hover:bg-red-500/10">Delete</button>
			</div>
		{/each}
	{/if}
</div>
